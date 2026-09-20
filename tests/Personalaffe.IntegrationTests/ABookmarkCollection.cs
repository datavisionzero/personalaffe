using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Personalaffe.IntegrationTests;

internal sealed class ABookmarkCollection(AnInstance instance, HttpClient owner) : IAsyncDisposable
{
    public AnInstance Instance { get; } = instance;
    public HttpClient Owner { get; } = owner;
    public static async Task<ABookmarkCollection> StartedAsync(PostgresFixture postgres)
    {
        var instance = await AnInstance.StartedAsync(postgres);
        return new(instance, await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken));
    }
    public Task<Answer> Folder(string name, Guid? parent = null, bool isPrivate = false, bool context = false) =>
        Send(HttpMethod.Post, "/api/bookmarks/folders", new { name, parent, @private = isPrivate }, context: context);
    public Task<Answer> Link(string title, Guid? folder = null, bool context = false) =>
        Send(HttpMethod.Post, "/api/bookmarks", new { title, url = "https://example.com/" + title, description = "A description", folder }, context: context);
    public async Task<Answer> Send(HttpMethod method, string path, object? body = null, string? version = null, bool context = false, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (version is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", version);
        }

        if (context)
        {
            request.Headers.Add("Personalaffe-Private", "true");
        }

        using var response = await (client ?? Owner).SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new(response.StatusCode, string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text), response.Headers.ETag?.ToString());
    }
    public async Task<HttpClient> Agent(string permission)
    {
        var issued = await Send(HttpMethod.Post, "/api/agents", new { name = Guid.NewGuid().ToString(), permissions = new { bookmarks = permission } });
        Assert.Equal(HttpStatusCode.Created, issued.Status);
        var client = Instance.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", issued.Body!["token"]!.GetValue<string>());
        return client;
    }
    public async ValueTask DisposeAsync() { Owner.Dispose(); await Instance.DisposeAsync(); }
    public sealed record Answer(HttpStatusCode Status, JsonNode? Body, string? Version)
    {
        public Guid Id => Body!["id"]!.GetValue<Guid>();
    }
}
