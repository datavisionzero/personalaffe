using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Sign-in, and the one thing it must never do: say which part of the attempt
/// was wrong.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SignInTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Every_way_of_being_wrong_gets_the_same_answer()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        var answers = new List<string>();

        foreach (var attempt in new[]
        {
            new { email = AnOwner.Address, password = "the wrong password entirely" },
            new { email = "somebody-else@example.com", password = AnOwner.Secret },
            new { email = "somebody-else@example.com", password = "the wrong password entirely" },
        })
        {
            using var client = AnOwner.AsABrowser(instance);
            using var response = await client.PostAsJsonAsync(
                "/api/session", attempt, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));

            answers.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // Byte for byte. An answer that differed would say whether an address
        // is the owner's, to whoever can reach the port.
        Assert.Single(answers.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_instance_with_no_owner_refuses_sign_in_the_same_way()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = AnOwner.AsABrowser(instance);

        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("/problems/unauthenticated", problem["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_shift_key_does_not_lock_the_owner_out()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        using var client = AnOwner.AsABrowser(instance);
        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new { email = "  OWNER@Example.COM ", password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Guessing_is_throttled_before_the_password_is_reached()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        using var client = AnOwner.AsABrowser(instance);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await client.PostAsJsonAsync(
                "/api/session",
                new { email = AnOwner.Address, password = $"wrong password number {attempt}" },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // The right password, and still no. The throttle says nothing about
        // itself: telling a guesser they are being throttled tells them they
        // have found something worth guessing at.
        using var correct = await client.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }

    [Fact]
    public async Task What_is_stored_is_a_digest_and_never_the_secret_in_the_cookie()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

        using var client = AnOwner.AsABrowser(instance);
        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var secret = cookie.Split(';')[0].Split('=', 2)[1];

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var session = await context.BrowserSessions.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(32, session.SecretHash.Length);
        Assert.DoesNotContain(secret, Convert.ToBase64String(session.SecretHash), StringComparison.Ordinal);
        Assert.Equal(AnOwner.Browser, session.Description);
        Assert.Null(session.RevokedAt);

        // Nor does it reach the log.
        Assert.DoesNotContain(instance.Warnings, line => line.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_revoked_session_stops_working_at_once()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var before = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await using (var context = AnInstance.ContextFor(instance.ConnectionString))
        {
            var session = await context.BrowserSessions.SingleAsync(TestContext.Current.CancellationToken);
            session.Revoke(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var after = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
