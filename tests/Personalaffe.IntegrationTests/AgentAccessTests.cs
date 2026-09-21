using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Agent access end to end: handed out, used, narrowed, reissued and revoked —
/// and every owner-only door it cannot open.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AgentAccessTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_agent_is_let_in_with_a_token_that_is_shown_once()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (agent, token) = await GrantedAsync(owner, "the deploy agent", read: "knowledge");

        Assert.StartsWith(TokenSecret.Prefix, token, StringComparison.Ordinal);
        Assert.Equal(token[..TokenSecret.PrefixLength], agent["token_prefix"]!.GetValue<string>());
        Assert.Equal("read", agent["permissions"]!["knowledge"]!.GetValue<string>());
        Assert.Equal("none", agent["permissions"]!["tasks"]!.GetValue<string>());

        // In the list, and the token is not.
        var listed = JsonNode.Parse(await owner.GetStringAsync(
            "/api/agents", TestContext.Current.CancellationToken))!.AsArray();

        Assert.Single(listed);
        Assert.DoesNotContain(token, listed.ToJsonString(), StringComparison.Ordinal);

        // Nor is it in the database.
        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var stored = await context.AgentAccess.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TokenSecret.HashOf(token), stored.TokenHash);
        Assert.DoesNotContain(instance.Warnings, line => line.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_token_admits_the_agent_and_says_what_it_reaches()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (_, token) = await GrantedAsync(owner, "an agent", read: "knowledge");

        using var agent = Holding(instance, token);

        var me = await agent.GetFromJsonAsync<JsonNode>("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal("agent", me!["kind"]!.GetValue<string>());
        Assert.Equal("an agent", me["name"]!.GetValue<string>());
        Assert.Equal("read", me["permissions"]!["knowledge"]!.GetValue<string>());

        // The owner's address is not an agent's to know.
        Assert.Null(me["email"]);
    }

    [Fact]
    public async Task An_agent_cannot_open_any_of_the_owners_doors()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (granted, token) = await GrantedAsync(owner, "an agent", read: "knowledge");
        var id = granted["id"]!.GetValue<string>();

        using var agent = Holding(instance, token);

        // Not by a permission that could be granted: no permission for any of
        // this exists. An agent that could issue a credential could issue
        // itself a better one.
        foreach (var (method, address) in new[]
        {
            (HttpMethod.Get, "/api/agents"),
            (HttpMethod.Post, "/api/agents"),
            (HttpMethod.Patch, $"/api/agents/{id}"),
            (HttpMethod.Post, $"/api/agents/{id}/token"),
            (HttpMethod.Delete, $"/api/agents/{id}"),
            (HttpMethod.Get, "/api/security"),
            (HttpMethod.Post, "/api/security/second-factor"),
            (HttpMethod.Post, "/api/security/second-factor/off"),
            (HttpMethod.Post, "/api/security/recovery-codes"),
            (HttpMethod.Post, "/api/security/password"),
            (HttpMethod.Put, "/api/security/inactivity-lock"),
            (HttpMethod.Get, "/api/sessions"),
            (HttpMethod.Delete, "/api/sessions"),
            (HttpMethod.Delete, "/api/session"),
        })
        {
            using var request = new HttpRequestMessage(method, address)
            {
                Content = JsonContent.Create(new { password = AnOwner.Secret }),
            };

            using var response = await agent.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_revoked_token_stops_working_and_the_row_stays()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (granted, token) = await GrantedAsync(owner, "an agent", read: "knowledge");
        var id = granted["id"]!.GetValue<string>();

        using var agent = Holding(instance, token);

        using var before = await agent.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var revoked = await owner.DeleteAsync(
            $"/api/agents/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.NotNull(JsonNode.Parse(
            await revoked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["revoked_at"]);

        using var after = await agent.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // A revoked access still names the agent everywhere it ever acted.
        var listed = JsonNode.Parse(await owner.GetStringAsync(
            "/api/agents", TestContext.Current.CancellationToken))!.AsArray();
        Assert.Single(listed);
        Assert.Equal("an agent", listed[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reissuing_a_token_stops_the_one_before_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (granted, first) = await GrantedAsync(owner, "an agent", read: "knowledge");
        var id = granted["id"]!.GetValue<string>();

        using var reissued = await owner.PostAsJsonAsync(
            $"/api/agents/{id}/token", new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reissued.StatusCode);

        var second = JsonNode.Parse(
            await reissued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!
            ["token"]!.GetValue<string>();

        Assert.NotEqual(first, second);

        using var old = Holding(instance, first);
        using var refused = await old.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using var now = Holding(instance, second);
        using var admitted = await now.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
    }

    [Fact]
    public async Task Changing_what_an_agent_reaches_takes_effect_on_the_next_request()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (granted, token) = await GrantedAsync(owner, "an agent", read: "knowledge");
        var id = granted["id"]!.GetValue<string>();

        using var narrowed = await owner.PatchAsJsonAsync(
            $"/api/agents/{id}",
            new
            {
                name = "a renamed agent",
                permissions = new
                {
                    scratchpad = "read_write",
                    knowledge = "none",
                    tasks = "none",
                    files = "none",
                },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, narrowed.StatusCode);

        using var agent = Holding(instance, token);
        var me = await agent.GetFromJsonAsync<JsonNode>("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal("a renamed agent", me!["name"]!.GetValue<string>());
        Assert.Equal("read_write", me["permissions"]!["scratchpad"]!.GetValue<string>());
        Assert.Equal("none", me["permissions"]!["knowledge"]!.GetValue<string>());
    }

    [Fact]
    public async Task Two_agents_cannot_share_a_name_however_it_is_capitalized()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        await GrantedAsync(owner, "Deploy", read: "knowledge");

        using var again = await owner.PostAsJsonAsync(
            "/api/agents",
            new { name = "deploy", permissions = Reading("knowledge") },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_permission_outside_the_set_is_refused_at_the_door()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var response = await owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new
                {
                    scratchpad = "admin",
                    knowledge = "none",
                    tasks = "none",
                    files = "none",
                },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_token_that_is_not_one_of_ours_never_reaches_the_database()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        foreach (var presented in new[]
        {
            "not-a-token",
            "pea_short",
            TokenSecret.Prefix + new string('x', TokenSecret.RandomLength),
        })
        {
            using var client = Holding(instance, presented);
            using var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task An_agent_never_needs_to_prove_where_its_write_came_from()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (_, token) = await GrantedAsync(owner, "an agent", read: "knowledge");

        // No CSRF header and no Origin: nothing attaches a bearer token but the
        // client that holds it, so the browser write guard does not apply.
        using var agent = Holding(instance, token);

        using var response = await agent.DeleteAsync("/api/session", TestContext.Current.CancellationToken);

        // Refused for what it is — an agent has no session — and not for how it
        // was headed.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static object Reading(string application) => new
    {
        scratchpad = application == "scratchpad" ? "read" : "none",
        knowledge = application == "knowledge" ? "read" : "none",
        tasks = application == "tasks" ? "read" : "none",
        files = application == "files" ? "read" : "none",
    };

    private static async Task<(JsonNode Agent, string Token)> GrantedAsync(
        HttpClient owner, string name, string read)
    {
        using var response = await owner.PostAsJsonAsync(
            "/api/agents",
            new { name, permissions = Reading(read) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        return (body["agent"]!, body["token"]!.GetValue<string>());
    }

    private static HttpClient Holding(AnInstance instance, string token)
    {
        var client = instance.CreateClient();

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        return client;
    }
}
