using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;
using Personalaffe.Application.Acts;
using Personalaffe.Application.Acts.Files;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>What one request to the Files API answered.</summary>
internal sealed record TheAnswer(HttpStatusCode Status, JsonNode? Body, string? ETag)
{
    /// <summary>The refusal's code, for a request that was refused.</summary>
    public string? Code => Body?["type"]?.GetValue<string>()?.Split('/')[^1];

    public string Detail => Body?["detail"]?.GetValue<string>() ?? string.Empty;

    public Guid Id => Body!["id"]!.GetValue<Guid>();

    /// <summary>The version of what was answered, as the next write sends it.</summary>
    public string Version => ETag ?? EntityTags.For(
        ContentVersion.Of(Body!["updated_at"]!.GetValue<DateTimeOffset>()));
}

/// <summary>
/// An instance with a storage root of its own, and the ten addresses of the
/// Files API as one object a test can read.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The storage root is a temporary directory per test</strong> and is
/// removed with the instance. Files are the first application whose state is
/// not all in the database, so a suite that shared one root would be a suite
/// whose tests could see each other's bytes — and the tidy-up, which removes
/// what no row points at, would remove another test's file.
/// </para>
/// <para>
/// It is a harness and not a second client: every method here sends the request
/// a `curl` would send, so what is under test is the API and never a wrapper
/// that agrees with it.
/// </para>
/// </remarks>
internal sealed class AFileArea : IAsyncDisposable
{
    private const string Files = "/api/files";

    private AFileArea(AnInstance instance, HttpClient owner, string root)
    {
        Instance = instance;
        Owner = owner;
        Root = root;
    }

    public AnInstance Instance { get; }

    /// <summary>The owner, signed in through a browser.</summary>
    public HttpClient Owner { get; }

    /// <summary>The storage root, so that a test can look at the volume itself.</summary>
    public string Root { get; }

    public static async Task<AFileArea> StartedAsync(
        PostgresFixture postgres,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"personalaffe-files-{Guid.NewGuid():n}");
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [StorageSettings.Variable] = root,
        };

        foreach (var pair in configuration ?? new Dictionary<string, string?>())
        {
            settings[pair.Key] = pair.Value;
        }

        var instance = AnInstance.Configured(await postgres.CreateDatabaseAsync(), settings);
        var owner = await AnOwner.SignedInAsync(instance, cancellationToken);

        return new AFileArea(instance, owner, root);
    }

    /// <summary>A second credential, with exactly this much access to Files.</summary>
    public async Task<HttpClient> AnAgentReachingAsync(string files, CancellationToken cancellationToken)
    {
        using var granted = await Owner.PostAsJsonAsync(
            "/api/agents",
            new { name = $"an agent {Guid.NewGuid():n}"[..20], permissions = new { files } },
            cancellationToken);

        var token = JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(cancellationToken))!["token"]!.GetValue<string>();

        var client = Instance.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    public Task<TheAnswer> ListAsync(Guid? folder, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Get, folder is { } id ? $"{Files}?folder={id}" : Files),
            cancellationToken,
            asWhom);

    public Task<TheAnswer> MakeFolderAsync(
        string? name, Guid? parent, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"{Files}/folders")
            {
                Content = JsonContent.Create(new { name, parent }),
            },
            cancellationToken,
            asWhom);

    public Task<TheAnswer> UploadAsync(
        string name,
        byte[] content,
        Guid? folder,
        CancellationToken cancellationToken,
        string mediaType = "application/octet-stream",
        HttpClient? asWhom = null)
    {
        var address = $"{Files}/content?name={Uri.EscapeDataString(name)}"
            + (folder is { } id ? $"&folder={id}" : string.Empty);

        var body = new ByteArrayContent(content);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);

        return SendAsync(
            new HttpRequestMessage(HttpMethod.Post, address) { Content = body },
            cancellationToken,
            asWhom);
    }

    /// <summary>The file's bytes, exactly as the instance answered them.</summary>
    public async Task<(HttpStatusCode Status, byte[] Content, HttpResponseHeaders Headers, HttpContentHeaders Content2)>
        DownloadAsync(Guid id, CancellationToken cancellationToken, HttpClient? asWhom = null)
    {
        using var response = await (asWhom ?? Owner).GetAsync(
            $"{Files}/{id}/content", cancellationToken);

        return (
            response.StatusCode,
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            response.Headers,
            response.Content.Headers);
    }

    public Task<TheAnswer> ReadAsync(Guid id, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Files}/{id}"), cancellationToken, asWhom);

    public Task<TheAnswer> ChangeFileAsync(
        Guid id,
        string? name,
        Guid? folder,
        string version,
        CancellationToken cancellationToken,
        HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Put, $"{Files}/{id}")
            {
                Content = JsonContent.Create(new { name, folder }),
            },
            version,
            cancellationToken,
            asWhom);

    public Task<TheAnswer> ChangeFolderAsync(
        Guid id,
        string? name,
        Guid? parent,
        string version,
        CancellationToken cancellationToken,
        HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Put, $"{Files}/folders/{id}")
            {
                Content = JsonContent.Create(new { name, folder = parent }),
            },
            version,
            cancellationToken,
            asWhom);

    public Task<TheAnswer> ReplaceAsync(
        Guid id,
        byte[] content,
        string version,
        CancellationToken cancellationToken,
        string mediaType = "application/octet-stream",
        HttpClient? asWhom = null)
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);

        return Guarded(
            new HttpRequestMessage(HttpMethod.Put, $"{Files}/{id}/content") { Content = body },
            version,
            cancellationToken,
            asWhom);
    }

    public Task<TheAnswer> DiscardFileAsync(
        Guid id, string version, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Delete, $"{Files}/{id}"), version, cancellationToken, asWhom);

    public Task<TheAnswer> DiscardFolderAsync(
        Guid id, string version, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Delete, $"{Files}/folders/{id}"),
            version,
            cancellationToken,
            asWhom);

    /// <summary>Switches Files on or off, as the owner.</summary>
    public async Task SwitchAsync(bool enabled, CancellationToken cancellationToken)
    {
        var applications = await Owner.GetFromJsonAsync<JsonNode>("/api/applications", cancellationToken);
        var state = applications!["items"]!.AsArray().First(
            item => item!["application"]!.GetValue<string>() == "files")!;

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/applications/files")
        {
            Content = JsonContent.Create(new { enabled }),
        };

        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch,
            EntityTags.For(ContentVersion.Of(state["updated_at"]!.GetValue<DateTimeOffset>())));

        using var response = await Owner.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Runs the hourly Trash sweep, now, through the act the background service
    /// calls.
    /// </summary>
    /// <remarks>
    /// The act and not the service: what a test about the Trash wants to know is
    /// what the purge does, and waiting an hour for a timer is not a thing a
    /// suite can do. That the service calls it on the hour and after a restart
    /// is <c>RetentionTests</c>'s subject.
    /// </remarks>
    public async Task<ThePurge> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = Instance.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PurgeTheTrash>()
            .ExecuteAsync(cancellationToken);
    }

    /// <summary>Runs the storage tidy-up, now, with the margin a test chooses.</summary>
    public async Task<int> TidyAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using var scope = Instance.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<TidyTheStorage>()
            .ExecuteAsync(olderThan, cancellationToken);
    }

    /// <summary>
    /// Moves a deletion back in time, so that the sweep's deadline has passed
    /// for it.
    /// </summary>
    /// <remarks>
    /// The alternative is a fake clock inside the instance, which would mean the
    /// rows under test were written by a clock no instance has. Moving the row
    /// is what an instance that had been running for a month would have.
    /// </remarks>
    public async Task BackdateAsync(TimeSpan by, CancellationToken cancellationToken)
    {
        await using var context = AnInstance.ContextFor(Instance.ConnectionString);
        var seconds = by.TotalSeconds;

        await context.Database.ExecuteSqlAsync(
            $"update files set deleted_at = deleted_at - make_interval(secs => {seconds}) where deleted_at is not null",
            cancellationToken);

        await context.Database.ExecuteSqlAsync(
            $"update folders set deleted_at = deleted_at - make_interval(secs => {seconds}) where deleted_at is not null",
            cancellationToken);
    }

    /// <summary>What is in the Trash, over every application.</summary>
    public async Task<JsonNode> TrashAsync(CancellationToken cancellationToken) =>
        (await Owner.GetFromJsonAsync<JsonNode>("/api/trash", cancellationToken))!;

    /// <summary>Puts something back, as the owner.</summary>
    public Task<TheAnswer> RestoreAsync(
        Guid id, string version, CancellationToken cancellationToken, string? restoreAs = null) =>
        Guarded(
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/trash/files/{id}/restore"
                + (restoreAs is null ? string.Empty : $"?name={Uri.EscapeDataString(restoreAs)}")),
            version,
            cancellationToken,
            asWhom: null);

    /// <summary>Removes one thing from the Trash for good, as the owner.</summary>
    public Task<TheAnswer> RemoveForGoodAsync(
        Guid id, string version, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/trash/files/{id}"),
            version,
            cancellationToken,
            asWhom);

    /// <summary>Every file this instance has on its volume, as addresses.</summary>
    public IReadOnlyList<string> OnTheVolume() =>
        Directory.Exists(Root)
            ? [.. Directory
                .EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)]
            : [];

    public ValueTask DisposeAsync()
    {
        Owner.Dispose();

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temporary directory that will not go is the operating system's
            // to clean up, not a reason to fail a test that has passed.
        }

        return Instance.DisposeAsync();
    }

    private Task<TheAnswer> Guarded(
        HttpRequestMessage request, string version, CancellationToken cancellationToken, HttpClient? asWhom)
    {
        request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, version);

        return SendAsync(request, cancellationToken, asWhom);
    }

    private async Task<TheAnswer> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken, HttpClient? asWhom)
    {
        using (request)
        {
            using var response = await (asWhom ?? Owner).SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            return new TheAnswer(
                response.StatusCode,
                text.Length == 0 ? null : JsonNode.Parse(text),
                response.Headers.ETag?.ToString());
        }
    }
}
