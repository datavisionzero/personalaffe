using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Personalaffe.IntegrationTests;

/// <summary>The server-enforced, session-specific half of the inactivity lock.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class InactivityLockTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ordinary_requests_do_not_extend_the_deadline_and_only_lock_operations_remain()
    {
        var clock = new MovingClock(Noon);
        await using var instance = await StartedAsync(clock);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        await EnableAsync(client, "0042", minutes: 5);

        clock.Advance(TimeSpan.FromMinutes(4));
        using (var ordinary = await client.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        using (var locked = await client.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal((HttpStatusCode)423, locked.StatusCode);
            Assert.Equal("/problems/locked", await TypeAsync(locked));
        }

        var status = await client.GetFromJsonAsync<JsonNode>(
            "/api/session/lock", TestContext.Current.CancellationToken);
        Assert.True(status!["enabled"]!.GetValue<bool>());
        Assert.True(status["locked"]!.GetValue<bool>());

        using (var security = await client.GetAsync("/api/security", TestContext.Current.CancellationToken))
        {
            Assert.Equal((HttpStatusCode)423, security.StatusCode);
        }

        using (var publicVersion = await client.GetAsync("/api/version", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, publicVersion.StatusCode);
        }

        using (var late = await client.PostAsJsonAsync(
            "/api/session/lock/activity", new { }, TestContext.Current.CancellationToken))
        {
            Assert.Equal((HttpStatusCode)423, late.StatusCode);
        }

        using var signedOut = await client.DeleteAsync(
            "/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
    }

    [Fact]
    public async Task Deliberate_activity_extends_the_deadline_while_a_late_report_never_does()
    {
        var clock = new MovingClock(Noon);
        await using var instance = await StartedAsync(clock);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        await EnableAsync(client, "0042", minutes: 5);

        clock.Advance(TimeSpan.FromMinutes(4));
        using (var active = await client.PostAsJsonAsync(
            "/api/session/lock/activity", new { }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, active.StatusCode);
        }

        clock.Advance(TimeSpan.FromMinutes(4));
        using (var stillOpen = await client.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        using (var boundary = await client.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal((HttpStatusCode)423, boundary.StatusCode);
        }
    }

    [Fact]
    public async Task A_wrong_pin_is_delayed_but_the_current_password_remains_an_independent_way_back()
    {
        var clock = new MovingClock(Noon);
        await using var instance = await StartedAsync(clock);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        await EnableAsync(client, "0042", minutes: 1);
        clock.Advance(TimeSpan.FromMinutes(1));

        using (var wrong = await UnlockAsync(client, pin: "9999"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, wrong.StatusCode);
            Assert.True(wrong.Headers.RetryAfter?.Delta > TimeSpan.Zero);
            Assert.Equal("/problems/throttled", await TypeAsync(wrong));
        }

        using (var blocked = await UnlockAsync(client, pin: "0042"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        }

        using (var recovered = await UnlockAsync(client, password: AnOwner.Secret))
        {
            Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
        }

        using var open = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);

        var security = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);
        Assert.True(security!["inactivity_lock_enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Lock_and_throttle_survive_a_server_restart()
    {
        var clock = new MovingClock(Noon);
        await using var first = await StartedAsync(clock);
        var (client, cookie) = await SignedInWithCookieAsync(first);
        using (client)
        {
            await EnableAsync(client, "0042", minutes: 1);
            clock.Advance(TimeSpan.FromMinutes(1));

            using var locked = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
            Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        }

        await using var second = AnInstance.AgainstWith(
            first.ConnectionString,
            services => services.AddSingleton<TimeProvider>(clock));
        using (var afterRestart = BrowserWithCookie(second, cookie))
        {
            var state = await afterRestart.GetFromJsonAsync<JsonNode>(
                "/api/session/lock", TestContext.Current.CancellationToken);
            Assert.True(state!["locked"]!.GetValue<bool>());

            using var wrong = await UnlockAsync(afterRestart, pin: "9999");
            Assert.Equal(HttpStatusCode.TooManyRequests, wrong.StatusCode);
        }

        await using var third = AnInstance.AgainstWith(
            first.ConnectionString,
            services => services.AddSingleton<TimeProvider>(clock));
        using var afterAnotherRestart = BrowserWithCookie(third, cookie);
        using var stillDelayed = await UnlockAsync(afterAnotherRestart, pin: "0042");
        Assert.Equal(HttpStatusCode.TooManyRequests, stillDelayed.StatusCode);

        clock.Advance(TimeSpan.FromSeconds(1));
        using var unlocked = await UnlockAsync(afterAnotherRestart, pin: "0042");
        Assert.Equal(HttpStatusCode.NoContent, unlocked.StatusCode);
    }

    [Fact]
    public async Task Tabs_share_one_session_while_other_browsers_unlock_independently()
    {
        var clock = new MovingClock(Noon);
        await using var instance = await StartedAsync(clock);
        using var one = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        using var other = await AnOwner.SignInAgainAsync(instance, TestContext.Current.CancellationToken);
        await EnableAsync(one, "0042", minutes: 1);
        clock.Advance(TimeSpan.FromMinutes(1));

        using (var oneLocked = await one.GetAsync("/api/me", TestContext.Current.CancellationToken))
        using (var otherLocked = await other.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal((HttpStatusCode)423, oneLocked.StatusCode);
            Assert.Equal((HttpStatusCode)423, otherLocked.StatusCode);
        }

        using (var unlocked = await UnlockAsync(one, pin: "0042"))
        {
            Assert.Equal(HttpStatusCode.NoContent, unlocked.StatusCode);
        }

        using (var mine = await one.GetAsync("/api/me", TestContext.Current.CancellationToken))
        using (var theirs = await other.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
            Assert.Equal((HttpStatusCode)423, theirs.StatusCode);
        }
    }

    [Fact]
    public async Task Concurrent_attempts_from_different_sessions_share_one_serialized_budget()
    {
        var clock = new MovingClock(Noon);
        await using var instance = await StartedAsync(clock);
        using var one = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        using var other = await AnOwner.SignInAgainAsync(instance, TestContext.Current.CancellationToken);
        await EnableAsync(one, "0042", minutes: 1);
        clock.Advance(TimeSpan.FromMinutes(1));

        var attempts = await Task.WhenAll(
            UnlockAsync(one, pin: "9999"),
            UnlockAsync(other, pin: "9999"));

        try
        {
            Assert.All(attempts, response => Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode));
        }
        finally
        {
            foreach (var response in attempts)
            {
                response.Dispose();
            }
        }

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, owner.PinUnlockFailures);
        Assert.Equal(Noon.AddMinutes(1).AddSeconds(1), owner.PinUnlockBlockedUntil);
    }

    [Fact]
    public async Task Changing_the_pin_keeps_this_browser_open_and_locks_the_others_on_the_new_version()
    {
        var clock = new MovingClock(Noon);
        await using var instance = await StartedAsync(clock);
        using var one = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        using var other = await AnOwner.SignInAgainAsync(instance, TestContext.Current.CancellationToken);
        await EnableAsync(one, "0042", minutes: 10);

        await EnableAsync(one, "567890", minutes: 10);

        using (var mine = await one.GetAsync("/api/me", TestContext.Current.CancellationToken))
        using (var theirs = await other.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
            Assert.Equal((HttpStatusCode)423, theirs.StatusCode);
        }

        using (var old = await UnlockAsync(other, pin: "0042"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, old.StatusCode);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        using var current = await UnlockAsync(other, pin: "567890");
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    [Fact]
    public async Task Agent_tokens_ignore_the_browser_lock_but_cannot_use_its_endpoints()
    {
        var clock = new MovingClock(Noon);
        await using var instance = await StartedAsync(clock);
        using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        var token = await GrantAgentAsync(owner);
        await EnableAsync(owner, "0042", minutes: 1);
        clock.Advance(TimeSpan.FromMinutes(1));

        using (var browser = await owner.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal((HttpStatusCode)423, browser.StatusCode);
        }

        using var agent = instance.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using (var work = await agent.GetAsync("/api/me", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, work.StatusCode);
        }

        using (var status = await agent.GetAsync("/api/session/lock", TestContext.Current.CancellationToken))
        using (var unlock = await agent.PostAsJsonAsync(
            "/api/session/lock/unlock", new { pin = "0042" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, unlock.StatusCode);
        }
    }

    [Fact]
    public async Task Unlock_needs_browser_write_protection_and_a_still_valid_session()
    {
        var clock = new MovingClock(Noon);
        await using var instance = await StartedAsync(clock);
        var (client, cookie) = await SignedInWithCookieAsync(instance);
        using (client)
        {
            await EnableAsync(client, "0042", minutes: 1);
        }
        clock.Advance(TimeSpan.FromMinutes(1));

        using (var unproved = instance.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = false }))
        {
            unproved.DefaultRequestHeaders.Add("Cookie", cookie);
            using var refused = await unproved.PostAsJsonAsync(
                "/api/session/lock/unlock", new { pin = "0042" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        clock.Advance(Personalaffe.Domain.BrowserSession.AbsoluteLifetime);
        using var expired = BrowserWithCookie(instance, cookie);
        using var notSignedIn = await UnlockAsync(expired, pin: "0042");
        Assert.Equal(HttpStatusCode.Unauthorized, notSignedIn.StatusCode);
    }

    private async Task<AnInstance> StartedAsync(MovingClock clock) =>
        await AnInstance.StartedWithAsync(
            postgres,
            services => services.AddSingleton<TimeProvider>(clock));

    private static async Task EnableAsync(HttpClient client, string pin, int minutes)
    {
        using var response = await client.PutAsJsonAsync(
            "/api/security/inactivity-lock",
            new
            {
                enabled = true,
                pin,
                inactivity_minutes = minutes,
                current_password = AnOwner.Secret,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static Task<HttpResponseMessage> UnlockAsync(
        HttpClient client, string? pin = null, string? password = null) =>
        client.PostAsJsonAsync(
            "/api/session/lock/unlock",
            new { pin, password },
            TestContext.Current.CancellationToken);

    private static async Task<(HttpClient Client, string Cookie)> SignedInWithCookieAsync(AnInstance instance)
    {
        await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);
        var client = instance.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = false });
        AddBrowserHeaders(client);

        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return (client, cookie);
    }

    private static HttpClient BrowserWithCookie(AnInstance instance, string cookie)
    {
        var client = instance.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = false });
        AddBrowserHeaders(client);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static void AddBrowserHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(Personalaffe.Api.Http.CsrfProtection.Header, "1");
        client.DefaultRequestHeaders.Add("User-Agent", AnOwner.Browser);
        client.DefaultRequestHeaders.Add(
            "Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
    }

    private static async Task<string> GrantAgentAsync(HttpClient owner)
    {
        using var response = await owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new
                {
                    scratchpad = "none",
                    knowledge = "read",
                    tasks = "none",
                    files = "none",
                },
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        return body["token"]!.GetValue<string>();
    }

    private static async Task<string> TypeAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        return body["type"]!.GetValue<string>();
    }

    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
