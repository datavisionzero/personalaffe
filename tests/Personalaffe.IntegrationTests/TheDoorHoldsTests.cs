using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The epic's claim, checked against the contract itself rather than a list
/// somebody maintains here.
/// </summary>
/// <remarks>
/// Every operation the instance says it has is driven without a credential and
/// with a revoked one. The list comes from <c>docs/api/openapi.json</c>, so an
/// endpoint added in a later epic is covered the day it is added — and an
/// endpoint that quietly loses its authentication turns this red rather than
/// being noticed by somebody reading a diff.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheDoorHoldsTests(PostgresFixture postgres)
{
    /// <summary>
    /// The five operations outside the door, and the whole of them. A sixth
    /// entry here is a decision somebody has to have made on purpose.
    /// </summary>
    private static readonly HashSet<string> Outside =
    [
        "GET /api/version",
        "GET /api/health/live",
        "GET /api/health/ready",
        "GET /api/setup",
        "POST /api/setup",
    ];

    public static TheoryData<string, string> EveryOperation
    {
        get
        {
            var operations = new TheoryData<string, string>();

            foreach (var (method, path) in Contract())
            {
                operations.Add(method, path);
            }

            return operations;
        }
    }

    /// <summary>
    /// Every operation the checked-in contract names. It is read rather than
    /// listed here so that an endpoint a later epic adds is covered the day it
    /// is added.
    /// </summary>
    private static IEnumerable<(string Method, string Path)> Contract()
    {
        var document = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot.Path, "docs", "api", "openapi.json")))!;

        foreach (var (path, methods) in document["paths"]!.AsObject())
        {
            foreach (var (method, _) in methods!.AsObject())
            {
                yield return (method.ToUpperInvariant(), path);
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryOperation))]
    public async Task Nothing_but_the_five_answers_without_a_credential(string method, string path)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        using var client = instance.CreateClient();
        using var response = await Sent(client, method, path, null);

        if (Outside.Contains($"{method} {path}"))
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            return;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // A 401 is an answer, and it is the same document every other refusal
        // is: a client branches on the code, and a client reporting skew reads
        // the version off whatever it got.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("Personalaffe-Version"));

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("/problems/unauthenticated", problem["type"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(EveryOperation))]
    public async Task A_revoked_token_is_no_better_than_no_token(string method, string path)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new
                {
                    scratchpad = "read_write",
                    knowledge = "read_write",
                    tasks = "read_write",
                    files = "read_write",
                },
            },
            TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        var token = body["token"]!.GetValue<string>();

        using var revoked = await owner.DeleteAsync(
            $"/api/agents/{body["agent"]!["id"]!.GetValue<string>()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        using var client = instance.CreateClient();
        using var response = await Sent(client, method, path, token);

        if (Outside.Contains($"{method} {path}"))
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            return;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_agent_with_everything_still_reaches_nothing_of_the_owners()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent with everything",
                permissions = new
                {
                    scratchpad = "read_write",
                    knowledge = "read_write",
                    tasks = "read_write",
                    files = "read_write",
                },
            },
            TestContext.Current.CancellationToken);

        var token = JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!
            ["token"]!.GetValue<string>();

        using var client = instance.CreateClient();

        // Read/write everywhere the owner can grant, and still not one of
        // these: issuing credentials and changing how the owner signs in are
        // not permissions, so they cannot be granted.
        foreach (var (method, path) in Contract().Where(operation =>
                     operation.Path.StartsWith("/api/agents", StringComparison.Ordinal)
                     || operation.Path.StartsWith("/api/security", StringComparison.Ordinal)
                     || operation.Path == "/api/sessions"
                     || operation.Path == "/api/sessions/{id}"))
        {
            using var response = await Sent(client, method, path, token);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task No_secret_anybody_holds_is_written_down_anywhere()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        var held = new List<string> { AnOwner.Secret };

        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var offered = await owner.PostAsJsonAsync(
            "/api/security/second-factor",
            new { password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        var secret = JsonNode.Parse(
            await offered.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!
            ["secret"]!.GetValue<string>();
        held.Add(secret);

        using var confirmed = await owner.PostAsJsonAsync(
            "/api/security/second-factor/confirm",
            new { code = Totp.CodeFor(secret, Totp.StepAt(DateTimeOffset.UtcNow)) },
            TestContext.Current.CancellationToken);

        held.AddRange(JsonNode.Parse(
            await confirmed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!
            ["recovery_codes"]!.AsArray().Select(code => code!.GetValue<string>()));

        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new
                {
                    scratchpad = "read",
                    knowledge = "none",
                    tasks = "none",
                    files = "none",
                },
            },
            TestContext.Current.CancellationToken);

        held.Add(JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!
            ["token"]!.GetValue<string>());

        // The session cookie's secret is held by the client; the one thing the
        // instance may not do is write any of these down.
        var log = string.Join("\n", instance.Logged);

        foreach (var value in held)
        {
            Assert.DoesNotContain(value, log, StringComparison.Ordinal);
        }

        // Nor may any of them be in the contract, which is published to
        // whoever asks.
        var contract = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot.Path, "docs", "api", "openapi.json"),
            TestContext.Current.CancellationToken);

        foreach (var value in held)
        {
            Assert.DoesNotContain(value, contract, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_request_log_says_what_was_asked_and_not_what_was_sent()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        using var client = AnOwner.AsABrowser(instance);
        using var refused = await client.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = "a password that is not the owner's" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        var log = string.Join("\n", instance.Logged);

        // This is a private workspace and its log is not a second copy of its
        // contents — or of what somebody typed at its door.
        Assert.DoesNotContain("a password that is not the owner's", log, StringComparison.Ordinal);
        Assert.Contains("/api/session", log, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> Sent(
        HttpClient client, string method, string path, string? token)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            // A path parameter that names nothing: what is being asked is who
            // may ask, and the answer must not depend on the thing existing.
            path.Replace("{id}", Guid.CreateVersion7().ToString(), StringComparison.Ordinal))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        if (token is not null)
        {
            request.Headers.Add("Authorization", $"Bearer {token}");
        }

        // Whatever a browser write has to carry, so that a refusal here is
        // never the CSRF guard standing in for the door.
        request.Headers.Add("X-Personalaffe-CSRF", "1");
        request.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
