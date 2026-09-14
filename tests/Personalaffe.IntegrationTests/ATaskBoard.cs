using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;
using Personalaffe.Application.Acts;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// An instance with task lists in it, and the nine addresses of the Tasks API
/// as one object a test can read.
/// </summary>
internal sealed class ATaskBoard : IAsyncDisposable
{
    private const string Lists = "/api/tasks/lists";

    private ATaskBoard(AnInstance instance, HttpClient owner)
    {
        Instance = instance;
        Owner = owner;
    }

    public AnInstance Instance { get; }

    /// <summary>The owner, signed in through a browser.</summary>
    public HttpClient Owner { get; }

    public static async Task<ATaskBoard> StartedAsync(
        PostgresFixture postgres,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var instance = configuration is null
            ? AnInstance.Against(await postgres.CreateDatabaseAsync())
            : AnInstance.Configured(await postgres.CreateDatabaseAsync(), configuration);

        var owner = await AnOwner.SignedInAsync(instance, cancellationToken);

        return new ATaskBoard(instance, owner);
    }

    /// <summary>A second credential, with exactly this much access to Tasks.</summary>
    public async Task<HttpClient> AnAgentReachingAsync(string tasks, CancellationToken cancellationToken)
    {
        using var granted = await Owner.PostAsJsonAsync(
            "/api/agents",
            new { name = $"an agent {Guid.NewGuid():n}"[..20], permissions = new { tasks } },
            cancellationToken);

        var token = JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(cancellationToken))!["token"]!.GetValue<string>();

        var client = Instance.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    public Task<TheAnswer> ListsAsync(CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, Lists), cancellationToken, asWhom);

    public Task<TheAnswer> MakeListAsync(
        string? name, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Post, Lists) { Content = JsonContent.Create(new { name }) },
            cancellationToken,
            asWhom);

    public Task<TheAnswer> RenameListAsync(
        Guid id, string? name, string version, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Put, $"{Lists}/{id}")
            {
                Content = JsonContent.Create(new { name }),
            },
            version,
            cancellationToken,
            asWhom);

    public Task<TheAnswer> DiscardListAsync(
        Guid id, string version, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Delete, $"{Lists}/{id}"), version, cancellationToken, asWhom);

    public Task<TheAnswer> TasksAsync(
        Guid list, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{Lists}/{list}/tasks"), cancellationToken, asWhom);

    public Task<TheAnswer> CaptureAsync(
        Guid list,
        string? title,
        CancellationToken cancellationToken,
        string? description = null,
        string? dueOn = null,
        HttpClient? asWhom = null) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"{Lists}/{list}/tasks")
            {
                Content = JsonContent.Create(new { title, description, due_on = dueOn }),
            },
            cancellationToken,
            asWhom);

    public Task<TheAnswer> ReadAsync(Guid id, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/tasks/{id}"), cancellationToken, asWhom);

    /// <summary>
    /// The one write, carrying all of it. Whatever is not given is what the
    /// task already says, which is what both real clients send.
    /// </summary>
    public async Task<TheAnswer> ChangeAsync(
        Guid id,
        string version,
        CancellationToken cancellationToken,
        Guid? list = null,
        string? title = null,
        string? description = null,
        string? dueOn = null,
        bool? completed = null,
        Guid? after = null,
        bool toTheTop = false,
        HttpClient? asWhom = null)
    {
        var now = await ReadAsync(id, cancellationToken, asWhom);
        var it = now.Body!;

        var body = new
        {
            list = list ?? it["list"]!.GetValue<Guid>(),
            title = title ?? it["title"]!.GetValue<string>(),
            description = description ?? it["description"]!.GetValue<string>(),
            due_on = dueOn ?? it["due_on"]?.GetValue<string>(),
            completed = completed ?? it["completed"]!.GetValue<bool>(),
            after = toTheTop ? null : after ?? it["after"]?.GetValue<Guid>(),
        };

        return await Guarded(
            new HttpRequestMessage(HttpMethod.Put, $"/api/tasks/{id}") { Content = JsonContent.Create(body) },
            version,
            cancellationToken,
            asWhom);
    }

    public Task<TheAnswer> DiscardAsync(
        Guid id, string version, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/tasks/{id}"), version, cancellationToken, asWhom);

    /// <summary>The titles in a list, in the order the instance answered them.</summary>
    public async Task<IReadOnlyList<string>> OrderAsync(Guid list, CancellationToken cancellationToken) =>
        [.. (await TasksAsync(list, cancellationToken)).Body!["items"]!.AsArray()
            .Select(task => task!["title"]!.GetValue<string>())];

    /// <summary>What is in the Trash, over every application.</summary>
    public async Task<JsonNode> TrashAsync(CancellationToken cancellationToken) =>
        (await Owner.GetFromJsonAsync<JsonNode>("/api/trash", cancellationToken))!;

    public Task<TheAnswer> RestoreAsync(
        Guid id, string version, CancellationToken cancellationToken, string? restoreAs = null) =>
        Guarded(
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/trash/tasks/{id}/restore"
                + (restoreAs is null ? string.Empty : $"?name={Uri.EscapeDataString(restoreAs)}")),
            version,
            cancellationToken,
            asWhom: null);

    /// <summary>Runs the hourly Trash sweep, now, through the act the service calls.</summary>
    public async Task<ThePurge> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = Instance.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PurgeTheTrash>()
            .ExecuteAsync(cancellationToken);
    }

    /// <summary>Moves every deletion back in time, past the sweep's deadline.</summary>
    public async Task BackdateAsync(TimeSpan by, CancellationToken cancellationToken)
    {
        await using var context = AnInstance.ContextFor(Instance.ConnectionString);
        var seconds = by.TotalSeconds;

        // Two statements rather than one over a table name in a string: a
        // table name is not a parameter, and interpolating one is the habit
        // this repository's analyzers exist to stop.
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlAsync(
            context.Database,
            $"update tasks set deleted_at = deleted_at - make_interval(secs => {seconds}) where deleted_at is not null",
            cancellationToken);

        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlAsync(
            context.Database,
            $"update task_lists set deleted_at = deleted_at - make_interval(secs => {seconds}) where deleted_at is not null",
            cancellationToken);
    }

    /// <summary>Switches Tasks on or off, as the owner.</summary>
    public async Task SwitchAsync(bool enabled, CancellationToken cancellationToken)
    {
        var applications = await Owner.GetFromJsonAsync<JsonNode>("/api/applications", cancellationToken);
        var state = applications!["items"]!.AsArray().First(
            item => item!["application"]!.GetValue<string>() == "tasks")!;

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/applications/tasks")
        {
            Content = JsonContent.Create(new { enabled }),
        };

        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch,
            EntityTags.For(ContentVersion.Of(state["updated_at"]!.GetValue<DateTimeOffset>())));

        using var response = await Owner.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public ValueTask DisposeAsync()
    {
        Owner.Dispose();

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
