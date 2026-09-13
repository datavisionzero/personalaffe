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
                "/api/health/live",
                "/api/health/ready",
                "/api/me",
                "/api/session",
                "/api/setup",
                "/api/version",
            ],
            paths);

        var schemas = document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key).ToHashSet();
        Assert.Contains("VersionResponse", schemas);
        Assert.Contains("HealthResponse", schemas);
        Assert.Contains("SetupStateResponse", schemas);
        Assert.Contains("SetupRequest", schemas);
        Assert.Contains("SignInRequest", schemas);
        Assert.Contains("MeResponse", schemas);
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
