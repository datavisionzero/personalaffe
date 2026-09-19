using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Personalaffe.Api.Http;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The door itself: what is outside it, what is behind it, and what a refusal
/// at it looks like.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DoorTests(PostgresFixture postgres)
{
    /// <summary>
    /// The whole of what an instance answers without a credential. A further
    /// address appearing here is a decision somebody has to have made on
    /// purpose — <c>/api/appearance</c> is the one PERSONAL-72 made, and what
    /// it gives away is a word the owner wrote and one of seven colours.
    /// </summary>
    public static TheoryData<string> Outside =>
    [
        "/api/version",
        "/api/health/live",
        "/api/health/ready",
        "/api/setup",
        "/api/appearance",
        "/api/openapi/v1.json",
    ];

    [Theory]
    [MemberData(nameof(Outside))]
    public async Task The_operations_outside_the_door_answer_without_one(string address)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var response = await client.GetAsync(address, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Everything_else_answers_unauthenticated_and_says_so_as_a_document()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        using var client = instance.CreateClient();
        using var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // A 401 is an answer, and a client reporting skew has to be able to read
        // the version off whatever it got.
        Assert.True(response.Headers.Contains("Personalaffe-Version"));

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("/problems/unauthenticated", problem["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_credential_that_is_not_one_admits_nobody()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        using var client = instance.CreateClient();

        client.DefaultRequestHeaders.Add("Cookie", $"{BrowserCookie.PlainName}=not-a-session");
        using var cookie = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, cookie.StatusCode);

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Authorization", "Bearer nothing-has-ever-been-issued");
        using var token = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, token.StatusCode);
    }

    [Fact]
    public async Task An_address_no_endpoint_took_is_still_a_not_found_and_not_a_challenge()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        // An address no endpoint will ever take. It used to be an application
        // that had not landed yet, which stopped being one the day it did.
        using var response = await client.GetAsync(
            "/api/nothing-is-here", TestContext.Current.CancellationToken);

        // Which endpoints this build has is in the contract, which anybody can
        // read. Answering 401 would stop a client telling "your credential is
        // wrong" from "this instance is older than you think".
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signing_in_admits_the_owner_and_the_instance_says_who_that_is()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var me = await client.GetFromJsonAsync<JsonNode>("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal("owner", me!["kind"]!.GetValue<string>());
        Assert.Equal(AnOwner.Address, me["email"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_cookie_is_http_only_and_carries_nothing_a_script_could_read()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        using var client = AnOwner.AsABrowser(instance);
        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.StartsWith($"{BrowserCookie.PlainName}=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        // Over plain HTTP a `secure` cookie is not stored at all, and the first
        // sign-in of a new installation often happens there.
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_it_came_in_on()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var out_ = await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, out_.StatusCode);

        using var after = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A_browser_write_that_does_not_prove_where_it_came_from_is_refused()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        client.DefaultRequestHeaders.Remove(CsrfProtection.Header);

        using var refused = await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // Refused, and nothing happened: the session is still the session.
        using var me = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_write_from_somebody_elses_page_is_refused_however_it_is_headed()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://somewhere-else.example.com");

        using var refused = await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_read_needs_no_such_proof()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        client.DefaultRequestHeaders.Remove(CsrfProtection.Header);
        client.DefaultRequestHeaders.Remove("Origin");

        using var me = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
