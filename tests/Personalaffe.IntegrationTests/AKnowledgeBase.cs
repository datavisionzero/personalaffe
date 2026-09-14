using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;
using Personalaffe.Application.Acts;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// An instance with a knowledge base in it, and the nine addresses of the
/// Knowledge API as one object a test can read.
/// </summary>
/// <remarks>
/// It is a harness and not a second client: every method sends the request a
/// `curl` would send, so what is under test is the API and never a wrapper that
/// agrees with it.
/// </remarks>
internal sealed class AKnowledgeBase : IAsyncDisposable
{
    private const string Pages = "/api/knowledge/pages";

    private AKnowledgeBase(AnInstance instance, HttpClient owner)
    {
        Instance = instance;
        Owner = owner;
    }

    public AnInstance Instance { get; }

    /// <summary>The owner, signed in through a browser.</summary>
    public HttpClient Owner { get; }

    public static async Task<AKnowledgeBase> StartedAsync(
        PostgresFixture postgres, CancellationToken cancellationToken)
    {
        var instance = AnInstance.Against(await postgres.CreateDatabaseAsync());
        var owner = await AnOwner.SignedInAsync(instance, cancellationToken);

        return new AKnowledgeBase(instance, owner);
    }

    /// <summary>A second credential, with exactly this much access to Knowledge.</summary>
    public async Task<HttpClient> AnAgentReachingAsync(string knowledge, CancellationToken cancellationToken)
    {
        using var granted = await Owner.PostAsJsonAsync(
            "/api/agents",
            new { name = $"an agent {Guid.NewGuid():n}"[..20], permissions = new { knowledge } },
            cancellationToken);

        var token = JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(cancellationToken))!["token"]!.GetValue<string>();

        var client = Instance.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    public Task<TheAnswer> TreeAsync(CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, Pages), cancellationToken, asWhom);

    public Task<TheAnswer> WriteAsync(
        string? title,
        Guid? parent,
        string? markdown,
        CancellationToken cancellationToken,
        HttpClient? asWhom = null) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Post, Pages)
            {
                Content = JsonContent.Create(new { title, parent, markdown }),
            },
            cancellationToken,
            asWhom);

    public Task<TheAnswer> ReadAsync(Guid id, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{Pages}/{id}"), cancellationToken, asWhom);

    public Task<TheAnswer> RewriteAsync(
        Guid id,
        string? title,
        Guid? parent,
        string? markdown,
        string version,
        CancellationToken cancellationToken,
        HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Put, $"{Pages}/{id}")
            {
                Content = JsonContent.Create(new { title, parent, markdown }),
            },
            version,
            cancellationToken,
            asWhom);

    public Task<TheAnswer> DiscardAsync(
        Guid id, string version, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Delete, $"{Pages}/{id}"), version, cancellationToken, asWhom);

    public Task<TheAnswer> HistoryAsync(
        Guid id, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{Pages}/{id}/revisions"), cancellationToken, asWhom);

    public Task<TheAnswer> OldVersionAsync(
        Guid id, Guid revision, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"{Pages}/{id}/revisions/{revision}"),
            cancellationToken,
            asWhom);

    public Task<TheAnswer> RecoverAsync(
        Guid id,
        Guid revision,
        string version,
        CancellationToken cancellationToken,
        HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Post, $"{Pages}/{id}/revisions/{revision}"),
            version,
            cancellationToken,
            asWhom);

    /// <summary>The export, as bytes, exactly as the instance answered it.</summary>
    public async Task<(HttpStatusCode Status, byte[] Zip, string? Disposition)> ExportAsync(
        CancellationToken cancellationToken, HttpClient? asWhom = null)
    {
        using var response = await (asWhom ?? Owner).GetAsync("/api/knowledge/export", cancellationToken);

        return (
            response.StatusCode,
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            response.Content.Headers.ContentDisposition?.ToString());
    }

    /// <summary>What is in the Trash, over every application.</summary>
    public async Task<JsonNode> TrashAsync(CancellationToken cancellationToken) =>
        (await Owner.GetFromJsonAsync<JsonNode>("/api/trash", cancellationToken))!;

    public Task<TheAnswer> RestoreAsync(
        Guid id, string version, CancellationToken cancellationToken, string? restoreAs = null) =>
        Guarded(
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/trash/knowledge/{id}/restore"
                + (restoreAs is null ? string.Empty : $"?name={Uri.EscapeDataString(restoreAs)}")),
            version,
            cancellationToken,
            asWhom: null);

    public Task<TheAnswer> RemoveForGoodAsync(
        Guid id, string version, CancellationToken cancellationToken, HttpClient? asWhom = null) =>
        Guarded(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/trash/knowledge/{id}"),
            version,
            cancellationToken,
            asWhom);

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

        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlAsync(
            context.Database,
            $"update pages set deleted_at = deleted_at - make_interval(secs => {seconds}) where deleted_at is not null",
            cancellationToken);
    }

    /// <summary>How many rows the two tables hold, whatever their state.</summary>
    public async Task<(int Pages, int Revisions)> RowsAsync(CancellationToken cancellationToken)
    {
        await using var context = AnInstance.ContextFor(Instance.ConnectionString);

        return (
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(
                    context.Pages),
                cancellationToken),
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
                context.PageRevisions, cancellationToken));
    }

    /// <summary>Switches Knowledge on or off, as the owner.</summary>
    public async Task SwitchAsync(bool enabled, CancellationToken cancellationToken)
    {
        var applications = await Owner.GetFromJsonAsync<JsonNode>("/api/applications", cancellationToken);
        var state = applications!["items"]!.AsArray().First(
            item => item!["application"]!.GetValue<string>() == "knowledge")!;

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/applications/knowledge")
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
