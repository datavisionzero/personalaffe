using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Application.Ports;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// An instance with something in all four applications, which is what the home
/// page and one search are about.
/// </summary>
/// <remarks>
/// Everything is written through the API a client writes through, so what these
/// suites read back is what an agent or a browser would have produced — and
/// the search reads columns Postgres computes on write, which a test that
/// inserted rows behind the API would quietly not be exercising.
/// </remarks>
internal sealed class AWorkspace : IAsyncDisposable
{
    private AWorkspace(AnInstance instance, HttpClient owner, string root)
    {
        Instance = instance;
        Owner = owner;
        Root = root;
    }

    public AnInstance Instance { get; }

    /// <summary>The owner, signed in through a browser.</summary>
    public HttpClient Owner { get; }

    /// <summary>Where the files go, so that no test writes into the checkout.</summary>
    public string Root { get; }

    public static Task<AWorkspace> StartedAsync(
        PostgresFixture postgres, CancellationToken cancellationToken) =>
        StartedAsync(postgres, registrations: null, cancellationToken);

    /// <summary>The same, with something else registered — a sky that is a test's.</summary>
    public static async Task<AWorkspace> StartedAsync(
        PostgresFixture postgres,
        Action<IServiceCollection>? registrations,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"personalaffe-workspace-{Guid.NewGuid():n}");

        var configuration = new Dictionary<string, string?> { [StorageSettings.Variable] = root };

        foreach (var pair in settings ?? new Dictionary<string, string?>())
        {
            configuration[pair.Key] = pair.Value;
        }

        var instance = registrations is null
            ? AnInstance.Configured(await postgres.CreateDatabaseAsync(), configuration)
            : await AnInstance.StartedWithAsync(postgres, registrations, configuration);

        var owner = await AnOwner.SignedInAsync(instance, cancellationToken);

        return new AWorkspace(instance, owner, root);
    }

    /// <summary>A Scratchpad entry. Its id.</summary>
    public async Task<Guid> EntryAsync(string text, CancellationToken cancellationToken) =>
        await CreatedAsync(
            "/api/scratchpad/entries", new { text, pinned = false }, cancellationToken);

    /// <summary>A knowledge page. Its id.</summary>
    public async Task<Guid> PageAsync(
        string title, string markdown, CancellationToken cancellationToken) =>
        await CreatedAsync(
            "/api/knowledge/pages",
            new { title, parent = (Guid?)null, markdown },
            cancellationToken);

    /// <summary>A task list. Its id.</summary>
    public async Task<Guid> ListAsync(string name, CancellationToken cancellationToken) =>
        await CreatedAsync("/api/tasks/lists", new { name }, cancellationToken);

    /// <summary>A task in a list. Its id.</summary>
    public async Task<Guid> TaskAsync(
        Guid list,
        string title,
        string description,
        DateOnly? dueOn,
        CancellationToken cancellationToken) =>
        await CreatedAsync(
            $"/api/tasks/lists/{list}/tasks",
            new { title, description, due_on = dueOn?.ToString("yyyy-MM-dd") },
            cancellationToken);

    /// <summary>A file with a name and some bytes in it. Its id.</summary>
    public async Task<Guid> FileAsync(
        string name, CancellationToken cancellationToken, string content = "some bytes")
    {
        using var body = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        using var response = await Owner.PostAsync(
            $"/api/files/content?name={Uri.EscapeDataString(name)}", body, cancellationToken);

        return Created(response, await Body(response, cancellationToken));
    }

    /// <summary>What one search found, as the document it answered with.</summary>
    public async Task<JsonNode> SearchAsync(string query, CancellationToken cancellationToken) =>
        JsonNode.Parse(await Owner.GetStringAsync(
            $"/api/search?q={Uri.EscapeDataString(query)}", cancellationToken))!;

    /// <summary>The titles one search found, in the order it found them.</summary>
    public async Task<IReadOnlyList<string>> FoundAsync(
        string query, CancellationToken cancellationToken) =>
        [.. (await SearchAsync(query, cancellationToken))["items"]!.AsArray()
            .Select(item => item!["title"]!.GetValue<string>())];

    /// <summary>The home page, as the document it answered with.</summary>
    public async Task<JsonNode> DashboardAsync(CancellationToken cancellationToken) =>
        JsonNode.Parse(await Owner.GetStringAsync("/api/dashboard", cancellationToken))!;

    /// <summary>The version an address is at, as the header a write sends back.</summary>
    public async Task<string> VersionOfAsync(string address, CancellationToken cancellationToken)
    {
        using var read = await Owner.GetAsync(address, cancellationToken);

        read.EnsureSuccessStatusCode();

        return read.Headers.ETag?.ToString()
            ?? throw new InvalidOperationException($"{address} answered without a version.");
    }

    /// <summary>Deletes something, holding the version it was just read at.</summary>
    public async Task DeleteAsync(string address, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, address);
        request.Headers.TryAddWithoutValidation(
            "If-Match", await VersionOfAsync(address, cancellationToken));

        using var response = await Owner.SendAsync(request, cancellationToken);

        if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.OK))
        {
            throw new InvalidOperationException(
                $"Deleting {address} answered {response.StatusCode}: "
                + await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    public async ValueTask DisposeAsync()
    {
        Owner.Dispose();
        await Instance.DisposeAsync();

        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private async Task<Guid> CreatedAsync(
        string address, object body, CancellationToken cancellationToken)
    {
        using var response = await Owner.PostAsJsonAsync(address, body, cancellationToken);

        return Created(response, await Body(response, cancellationToken));
    }

    private static Guid Created(HttpResponseMessage response, string body)
    {
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Creating through {response.RequestMessage?.RequestUri} answered "
                + $"{response.StatusCode}: {body}");
        }

        return JsonNode.Parse(body)!["id"]!.GetValue<Guid>();
    }

    private static Task<string> Body(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.Content.ReadAsStringAsync(cancellationToken);
}
