using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The contract as the running instance serves it, against the one checked in.
/// CI makes the same comparison; this makes it before the push, so that a
/// changed shape is a red test on the desk and not a red trunk.
/// </summary>
/// <remarks>
/// The comparison is structural, not textual: what the two documents say has to
/// agree, not how they were formatted. Regenerating is the same test with
/// <c>PERSONALAFFE_CAPTURE_CONTRACT=1</c>, which writes the served document over
/// the checked-in one — formatted the way CI's capture step formats it, so that
/// the two never differ by whitespace — and then passes.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ContractTests(PostgresFixture postgres)
{
    private const string CaptureVariable = "PERSONALAFFE_CAPTURE_CONTRACT";

    [Fact]
    public async Task The_document_is_served_without_a_credential_and_names_every_endpoint()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var response = await client.GetAsync("/api/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("personalaffe", document["info"]!["title"]!.GetValue<string>());

        // Whoever captured it was at some address; nobody else is.
        Assert.Null(document["servers"]);

        var paths = document["paths"]!.AsObject().Select(path => path.Key).Order(StringComparer.Ordinal);
        Assert.Equal(
            [
                "/api/agents",
                "/api/agents/{id}",
                "/api/agents/{id}/token",
                "/api/appearance",
                "/api/applications",
                "/api/applications/{application}",
                "/api/bookmarks",
                "/api/bookmarks/dashboard",
                "/api/bookmarks/export",
                "/api/bookmarks/folders",
                "/api/bookmarks/folders/{id}",
                "/api/bookmarks/import",
                "/api/bookmarks/import/preview",
                "/api/bookmarks/{id}",
                "/api/bookmarks/{id}/favorite",
                "/api/bookmarks/{id}/open",
                "/api/dashboard",
                "/api/dashboard/tiles/{tile}",
                "/api/files",
                "/api/files/content",
                "/api/files/folders",
                "/api/files/folders/{id}",
                "/api/files/{id}",
                "/api/files/{id}/content",
                "/api/health/live",
                "/api/health/ready",
                "/api/knowledge/export",
                "/api/knowledge/pages",
                "/api/knowledge/pages/{id}",
                "/api/knowledge/pages/{id}/revisions",
                "/api/knowledge/pages/{id}/revisions/{revision}",
                "/api/me",
                "/api/scratchpad/entries",
                "/api/scratchpad/entries/{id}",
                "/api/search",
                "/api/security",
                "/api/security/password",
                "/api/security/recovery-codes",
                "/api/security/second-factor",
                "/api/security/second-factor/confirm",
                "/api/security/second-factor/off",
                "/api/session",
                "/api/sessions",
                "/api/sessions/{id}",
                "/api/setup",
                "/api/tasks/lists",
                "/api/tasks/lists/{id}",
                "/api/tasks/lists/{id}/tasks",
                "/api/tasks/{id}",
                "/api/trash",
                "/api/trash/{application}/{id}",
                "/api/trash/{application}/{id}/restore",
                "/api/version",
                "/api/weather",
                "/api/weather/place",
                "/api/weather/places",
            ],
            paths);

        // No schema may be named after what a generated client will call an
        // operation's own response type. `oapi-codegen` names that
        // `<operationId>Response`, so an operation `Search` beside a schema
        // `SearchResponse` is two types with one name and a Go client that does
        // not compile — which is exactly what PERSONAL-E9 walked into.
        var operations = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject())
            .Select(operation => operation.Value!["operationId"]?.GetValue<string>())
            .Where(name => name is not null)
            .Select(name => $"{name}Response")
            .ToHashSet(StringComparer.Ordinal);

        var schemas = document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key).ToHashSet();

        Assert.Empty(schemas.Intersect(operations, StringComparer.Ordinal));

        Assert.Contains("VersionResponse", schemas);
        Assert.Contains("HealthResponse", schemas);
        Assert.Contains("SetupStateResponse", schemas);
        Assert.Contains("SetupRequest", schemas);
        Assert.Contains("SignInRequest", schemas);
        Assert.Contains("MeResponse", schemas);
        Assert.Contains("SecurityResponse", schemas);
        Assert.Contains("RecoveryCodesResponse", schemas);
        Assert.Contains("SessionResponse", schemas);
        Assert.Contains("AgentResponse", schemas);
        Assert.Contains("AgentTokenResponse", schemas);
        // `PermissionsShape` is the contract's shape of the Domain type of the
        // same name, and the suffix is dropped from the schema id
        // (OpenApiDocument). This is the first type to use that rule.
        Assert.Contains("Permissions", schemas);
        Assert.Contains("ApplicationsResponse", schemas);
        Assert.Contains("ApplicationResponse", schemas);
        Assert.Contains("SwitchApplicationRequest", schemas);
        Assert.Contains("TrashResponse", schemas);
        Assert.Contains("TrashEntryResponse", schemas);
        Assert.Contains("TrashEmptiedResponse", schemas);
        Assert.Contains("RestoredResponse", schemas);
        Assert.Contains("Actor", schemas);
        Assert.Contains("ScratchpadResponse", schemas);
        Assert.Contains("ScratchpadEntryResponse", schemas);
        Assert.Contains("CaptureEntryRequest", schemas);
        Assert.Contains("RewriteEntryRequest", schemas);
        Assert.Contains("FilesResponse", schemas);
        Assert.Contains("FileResponse", schemas);
        Assert.Contains("FolderResponse", schemas);
        Assert.Contains("MakeFolderRequest", schemas);
        Assert.Contains("ChangeRequest", schemas);
        Assert.Contains("TreeResponse", schemas);
        Assert.Contains("OutlineResponse", schemas);
        Assert.Contains("PageResponse", schemas);
        Assert.Contains("WritePageRequest", schemas);
        Assert.Contains("RewritePageRequest", schemas);
        Assert.Contains("HistoryResponse", schemas);
        Assert.Contains("RevisionResponse", schemas);
        Assert.Contains("OldVersionResponse", schemas);
        Assert.Contains("TaskListsResponse", schemas);
        Assert.Contains("TaskListResponse", schemas);
        Assert.Contains("TaskListRequest", schemas);
        Assert.Contains("TasksResponse", schemas);
        Assert.Contains("TaskResponse", schemas);
        Assert.Contains("CaptureTaskRequest", schemas);
        Assert.Contains("ChangeTaskRequest", schemas);
        Assert.Contains("Permission", schemas);
    }

    [Fact]
    public async Task The_served_document_is_the_checked_in_one()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        var served = JsonNode.Parse(
            await client.GetStringAsync("/api/openapi/v1.json", TestContext.Current.CancellationToken))!;

        var path = Path.Combine(RepositoryRoot.Path, "docs", "api", "openapi.json");

        if (Environment.GetEnvironmentVariable(CaptureVariable) is "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, Formatted(served), TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(path), $"{path} is missing; capture it with {CaptureVariable}=1.");

        var checkedIn = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        Assert.True(
            JsonNode.DeepEquals(served, checkedIn),
            "The instance serves a document other than docs/api/openapi.json. "
            + $"Regenerate it with {CaptureVariable}=1 and commit it with the change that caused it.");
    }

    /// <summary>
    /// Two-space indent, one member per line, nothing escaped that need not be,
    /// a newline at the end — the same bytes CI's Python formatting produces for
    /// the same document, so that a local capture and CI's never disagree.
    /// </summary>
    private static string Formatted(JsonNode document) =>
        document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n";
}
