using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// PERSONAL-E10's pass over the security surface as it now stands, with all
/// five applications and the search on it.
/// </summary>
/// <remarks>
/// <para>
/// The checks each epic made are still where they were made:
/// <see cref="TheDoorHoldsTests"/> drives every operation the contract names
/// without a credential and with a revoked one, <see cref="TrustedProxiesTests"/>
/// the sign-in throttle behind a proxy, <see cref="FilesTests"/> and
/// <see cref="TheFilesHoldTests"/> the upload limits and the names that try to
/// leave the storage root, <see cref="SearchTests"/> and
/// <see cref="ApplicationSwitchTests"/> a switched-off application, and
/// <see cref="TheLogTests"/> what a log line may carry. What is here is what
/// only the whole surface can be asked.
/// </para>
/// <para>
/// <strong>The route table rather than the contract.</strong> The suites above
/// walk <c>docs/api/openapi.json</c>, which is what the instance says it has.
/// This one walks what it actually mapped, so that an endpoint outside the
/// group — one that never reached the contract, or one that is not an API
/// endpoint at all — cannot be the hole nobody looked in.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheSecurityPassTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Everything this instance answers without asking who is asking, named one
    /// by one. An entry added here is a decision somebody has to have made on
    /// purpose.
    /// </summary>
    /// <remarks>
    /// The five operations of <c>docs/api.md</c>, <em>Operations</em>, and four
    /// endpoints that are not operations of the API: sign-in, which is how a
    /// caller gets a credential and which answers a refusal to anything but the
    /// right password; the contract, which a client compiles against before it
    /// has one; the group's own not-found, which says that this instance has no
    /// such endpoint rather than challenging for a credential it would refuse
    /// anyway; and the web application, whose shell is a bundle of JavaScript
    /// that holds nothing and asks the API for everything it shows.
    /// </remarks>
    private static readonly HashSet<string> Outside =
    [
        "GET /api/version",
        "GET /api/health/live",
        "GET /api/health/ready",
        "GET /api/setup",
        "POST /api/setup",
        "POST /api/session",
        "GET /api/openapi/{documentName}.json",
        "* /api/{*path:nonfile}",
        "GET {*path:nonfile}",
        "HEAD {*path:nonfile}",
    ];

    [Fact]
    public async Task Nothing_this_instance_mapped_is_outside_the_door_by_accident()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);

        // Asking for a client is what starts the host; the endpoints are built
        // by then and not before.
        using var started = instance.CreateClient();

        var open = new List<string>();

        foreach (var endpoint in instance.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>())
        {
            var behindTheDoor =
                endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
                && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

            if (behindTheDoor)
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;

            foreach (var method in methods is { Count: > 0 } ? methods : ["*"])
            {
                open.Add($"{method} {endpoint.RoutePattern.RawText}");
            }
        }

        // Named one by one, because "there are nine of them" is a check that
        // passes when one is swapped for another.
        Assert.Equal(Outside.Order(StringComparer.Ordinal), open.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_answer_says_what_a_browser_may_do_with_it_whatever_it_answered()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var file = await workspace.FileAsync("evidence.txt", Token);

        using var stranger = workspace.Instance.CreateClient();

        // The four answers that are shaped differently: the application's own
        // page, a JSON read, a download, and a refusal nobody authenticated
        // for. A header on three of them is a header on none of them.
        foreach (var (what, response) in new[]
        {
            ("the page", await stranger.GetAsync("/", Token)),
            ("a read", await workspace.Owner.GetAsync("/api/dashboard", Token)),
            ("a download", await workspace.Owner.GetAsync($"/api/files/{file}/content", Token)),
            ("a refusal", await stranger.GetAsync("/api/me", Token)),
        })
        {
            using (response)
            {
                Assert.Equal(
                    SecurityHeaders.ContentSecurityPolicy,
                    Header(response, "Content-Security-Policy", what));
                Assert.Equal("DENY", Header(response, "X-Frame-Options", what));
                Assert.Equal("nosniff", Header(response, "X-Content-Type-Options", what));
                Assert.Equal("no-referrer", Header(response, "Referrer-Policy", what));
                Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy", what));
                Assert.Equal(SecurityHeaders.PermissionsPolicy, Header(response, "Permissions-Policy", what));
            }
        }
    }

    [Fact]
    public void The_policy_admits_this_application_and_nothing_that_would_carry_anything_out()
    {
        // The three that make the difference between a policy and a header:
        // script from here only, so an injected tag is not run; nothing framed,
        // so nothing is clickjacked; and a connection to here only, so a script
        // that did somehow run cannot send what it read anywhere.
        Assert.Contains("script-src 'self';", SecurityHeaders.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none';", SecurityHeaders.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self'", SecurityHeaders.ContentSecurityPolicy, StringComparison.Ordinal);

        // And the one it has to admit, said here so that widening it is a line
        // in a diff: the Markdown editor writes its own stylesheets into the
        // document at runtime (ADR 0001, CodeMirror).
        Assert.Contains("style-src 'self' 'unsafe-inline';", SecurityHeaders.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", SecurityHeaders.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Over_HTTPS_the_session_is_bound_to_this_host_and_the_browser_is_asked_to_stay()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, Token);

        using var browser = instance.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://workspace.example.com") });

        using var signedIn = await browser.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            Token);

        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);

        var cookie = Assert.Single(signedIn.Headers.GetValues("Set-Cookie"));

        // `__Host-` is the prefix a browser refuses to store unless the cookie
        // is secure, path-wide and bound to this exact host: a name that cannot
        // be set by anything else answering for a neighbouring one.
        Assert.StartsWith($"{BrowserCookie.SecureName}=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            SecurityHeaders.StrictTransportSecurity,
            Assert.Single(signedIn.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Over_plain_HTTP_nothing_pins_a_host_that_may_not_have_a_certificate_yet()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, Token);

        using var browser = AnOwner.AsABrowser(instance);

        using var signedIn = await browser.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            Token);

        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);

        // The first sign-in of a new installation happens at
        // http://127.0.0.1:8080/ often enough that pinning it would be a way of
        // locking somebody out of their own instance an hour after they
        // installed it (BrowserCookie).
        Assert.StartsWith($"{BrowserCookie.PlainName}=", Assert.Single(signedIn.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
        Assert.False(signedIn.Headers.Contains("Strict-Transport-Security"));
    }

    /// <summary>
    /// Every write the contract names in the four applications, their Trash and
    /// the home page's preferences.
    /// </summary>
    public static TheoryData<string, string> EveryWriteAnAgentCouldTry
    {
        get
        {
            var writes = new TheoryData<string, string>();

            foreach (var (method, path) in Contract())
            {
                if (method is "GET" or "HEAD" or "OPTIONS")
                {
                    continue;
                }

                if (Reaches(path))
                {
                    writes.Add(method, path);
                }
            }

            return writes;
        }
    }

    [Fact]
    public void There_are_writes_in_all_four_applications_to_drive()
    {
        // Without this, an epic that renamed a prefix would leave the theory
        // below with nothing to drive and a suite that passes by having no
        // subject.
        foreach (var application in new[] { "scratchpad", "files", "knowledge", "tasks" })
        {
            Assert.Contains(
                Contract().Where(operation => operation.Method is not ("GET" or "HEAD" or "OPTIONS")),
                operation => operation.Path.StartsWith($"/api/{application}", StringComparison.Ordinal));
        }

        Assert.Contains(
            EveryWriteAnAgentCouldTry,
            row => row.Data.Item2 == "/api/dashboard/tiles/{tile}");
    }

    [Theory]
    [MemberData(nameof(EveryWriteAnAgentCouldTry))]
    public async Task A_credential_that_may_only_read_changes_nothing_anywhere(string method, string path)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        using var reader = await AnAgentAsync(instance, owner, "read");

        using var response = await Sent(reader, method, path);

        // Forbidden, and not "nothing at that address": what the caller may do
        // is decided before anything is looked up, so a read-only credential
        // cannot map the workspace by watching which ids answer 404.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("/problems/forbidden", await TypeOf(response));
    }

    [Fact]
    public async Task An_agent_revoked_while_it_is_working_stops_at_its_next_request_downloads_and_all()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var file = await workspace.FileAsync("payslip.pdf", Token, "some bytes nobody else should have");
        var address = $"/api/files/{file}/content";

        using var granted = await workspace.Owner.PostAsJsonAsync(
            "/api/agents",
            new { name = "an agent at work", permissions = new { files = "read_write" } },
            Token);

        var body = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!;

        using var agent = workspace.Instance.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", body["token"]!.GetValue<string>());

        // It is working, and the download is the address it now holds.
        using var downloaded = await agent.GetAsync(address, Token);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);

        using var revoked = await workspace.Owner.DeleteAsync(
            $"/api/agents/{body["agent"]!["id"]!.GetValue<string>()}", Token);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        // A download is not a grant. The address it already had is behind the
        // same door as everything else, and the door is shut now.
        using var refused = await agent.GetAsync(address, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("/problems/unauthenticated", await TypeOf(refused));

        using var alsoRefused = await agent.GetAsync("/api/search?q=payslip", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, alsoRefused.StatusCode);
    }

    [Fact]
    public async Task Two_clients_on_one_page_and_the_one_that_read_it_first_is_told_rather_than_obeyed()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var page = await workspace.PageAsync("Architecture", "what it says now", Token);
        var address = $"/api/knowledge/pages/{page}";

        // Both read it at the same version, which is what two people in two
        // browsers — or a browser and an agent — actually do.
        var version = await workspace.VersionOfAsync(address, Token);

        using var second = await AnOwner.SignInAgainAsync(workspace.Instance, Token);

        Assert.Equal(HttpStatusCode.OK, (await Wrote(workspace.Owner, address, "the first one's text", version)).Status);

        var (status, code) = await Wrote(second, address, "the second one's text", version);

        Assert.Equal(HttpStatusCode.PreconditionFailed, status);
        Assert.Equal("/problems/stale", code);

        // And nothing of the second write is in it: a guard that refused and
        // stored anyway would be worse than no guard at all.
        var current = await workspace.Owner.GetFromJsonAsync<JsonNode>(address, Token);
        Assert.Equal("the first one's text", current!["markdown"]!.GetValue<string>());

        // The history says one write happened, not two.
        var history = await workspace.Owner.GetFromJsonAsync<JsonNode>($"{address}/revisions", Token);
        Assert.Single(history!["items"]!.AsArray());
    }

    private static async Task<(HttpStatusCode Status, string? Code)> Wrote(
        HttpClient client, string address, string markdown, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, address)
        {
            Content = JsonContent.Create(new { title = "Architecture", parent = (Guid?)null, markdown }),
        };

        request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, version);

        using var response = await client.SendAsync(request, Token);

        return response.StatusCode == HttpStatusCode.OK
            ? (response.StatusCode, null)
            : (response.StatusCode, await TypeOf(response));
    }

    private static async Task<HttpClient> AnAgentAsync(AnInstance instance, HttpClient owner, string permission)
    {
        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = $"an agent that may {permission}",
                permissions = new
                {
                    scratchpad = permission,
                    knowledge = permission,
                    tasks = permission,
                    files = permission,
                },
            },
            Token);

        var token = JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(Token))!["token"]!.GetValue<string>();

        var agent = instance.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return agent;
    }

    /// <summary>The four applications, what they put in the Trash, and the home page's own setting.</summary>
    private static bool Reaches(string path) =>
        path.StartsWith("/api/scratchpad", StringComparison.Ordinal)
        || path.StartsWith("/api/files", StringComparison.Ordinal)
        || path.StartsWith("/api/knowledge", StringComparison.Ordinal)
        || path.StartsWith("/api/tasks", StringComparison.Ordinal)
        || path.StartsWith("/api/trash", StringComparison.Ordinal)
        || path.StartsWith("/api/dashboard/tiles", StringComparison.Ordinal);

    /// <summary>
    /// The query the contract says an operation cannot be called without.
    /// </summary>
    /// <remarks>
    /// An upload says what the file is called in its query, and a request
    /// missing it is refused as <c>validation</c> while the reader is still
    /// parsing — before anything has asked who is calling. A sweep that sent it
    /// without one would be reading that refusal as the one it is looking for.
    /// </remarks>
    private static string Required(string method, string path)
    {
        var operation = Document()["paths"]![path]![method.ToLowerInvariant()]!;

        var required = (operation["parameters"]?.AsArray() ?? [])
            .Where(parameter =>
                parameter!["in"]?.GetValue<string>() == "query"
                && parameter["required"]?.GetValue<bool>() == true)
            .Select(parameter => $"{parameter!["name"]!.GetValue<string>()}=whatever.txt")
            .ToList();

        return required.Count == 0 ? string.Empty : "?" + string.Join("&", required);
    }

    private static JsonNode Document() => JsonNode.Parse(File.ReadAllText(
        Path.Combine(RepositoryRoot.Path, "docs", "api", "openapi.json")))!;

    private static IEnumerable<(string Method, string Path)> Contract()
    {
        foreach (var (path, methods) in Document()["paths"]!.AsObject())
        {
            foreach (var (method, _) in methods!.AsObject())
            {
                yield return (method.ToUpperInvariant(), path);
            }
        }
    }

    private static async Task<HttpResponseMessage> Sent(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), AnAddress.Filled(path) + Required(method, path))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, EntityTags.For(ContentVersion.Of(DateTimeOffset.UtcNow)));

        return await client.SendAsync(request, Token);
    }

    private static string Header(HttpResponseMessage response, string name, string what)
    {
        if (response.Headers.TryGetValues(name, out var values)
            || response.Content.Headers.TryGetValues(name, out values))
        {
            return Assert.Single(values);
        }

        Assert.Fail($"{what} carried no {name}.");
        return string.Empty;
    }

    private static async Task<string> TypeOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["type"]!.GetValue<string>();
}
