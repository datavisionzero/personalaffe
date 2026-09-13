using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The second factor from both ends: enrolling it, signing in through it,
/// getting past it with a code off paper, and taking it off again.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SecurityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_new_instance_has_a_password_and_nothing_else()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var security = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);

        Assert.False(security!["second_factor_enabled"]!.GetValue<bool>());
        Assert.Null(security["enrolled_at"]);
        Assert.Equal(0, security["recovery_codes_remaining"]!.GetValue<int>());
    }

    [Fact]
    public async Task Enrolling_takes_two_steps_and_the_first_one_changes_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (secret, _) = await OfferedAsync(client);

        // Offered, not in force. A secret that took effect the moment it was
        // shown would lock the owner out on the day they mistyped it.
        var before = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);
        Assert.False(before!["second_factor_enabled"]!.GetValue<bool>());

        using var wrong = await client.PostAsJsonAsync(
            "/api/security/second-factor/confirm",
            new { code = "000000" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var still = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);
        Assert.False(still!["second_factor_enabled"]!.GetValue<bool>());

        var codes = await ConfirmedAsync(client, secret);

        Assert.Equal(RecoveryCode.Count, codes.Count);

        var after = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);
        Assert.True(after!["second_factor_enabled"]!.GetValue<bool>());
        Assert.NotNull(after["enrolled_at"]);
        Assert.Equal(RecoveryCode.Count, after["recovery_codes_remaining"]!.GetValue<int>());
    }

    [Fact]
    public async Task Nothing_about_signing_in_changes_without_the_password()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        // A browser left open is enough to take an instance over otherwise.
        foreach (var (address, body) in new (string, object)[]
        {
            ("/api/security/second-factor", new { password = "not the password" }),
            ("/api/security/second-factor/off", new { password = "not the password" }),
            ("/api/security/recovery-codes", new { password = "not the password" }),
            ("/api/security/password", new { current_password = "not it", password = "a new long password" }),
        })
        {
            using var refused = await client.PostAsJsonAsync(
                address, body, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
    }

    [Fact]
    public async Task With_a_second_factor_the_password_alone_is_not_enough()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (secret, _) = await OfferedAsync(client);
        await ConfirmedAsync(client, secret);

        using var another = AnOwner.AsABrowser(instance);

        using var asked = await another.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, asked.StatusCode);

        var problem = JsonNode.Parse(
            await asked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("/problems/second-factor", problem["type"]!.GetValue<string>());
        Assert.False(asked.Headers.Contains("Set-Cookie"));

        using var wrong = await another.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret, second_factor = "000000" },
            TestContext.Current.CancellationToken);
        Assert.Equal("/problems/unauthenticated", Type(await Body(wrong)));

        using var right = await another.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret, second_factor = Next(secret) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
    }

    [Fact]
    public async Task A_recovery_code_gets_in_once_and_then_is_spent()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (secret, _) = await OfferedAsync(client);
        var codes = await ConfirmedAsync(client, secret);

        using var first = AnOwner.AsABrowser(instance);
        using var admitted = await first.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret, second_factor = codes[0] },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, admitted.StatusCode);

        using var again = AnOwner.AsABrowser(instance);
        using var refused = await again.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret, second_factor = codes[0] },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        var left = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);
        Assert.Equal(RecoveryCode.Count - 1, left!["recovery_codes_remaining"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_fresh_set_of_codes_throws_the_old_set_away()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (secret, _) = await OfferedAsync(client);
        var old = await ConfirmedAsync(client, secret);

        using var reissued = await client.PostAsJsonAsync(
            "/api/security/recovery-codes",
            new { password = AnOwner.Secret },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reissued.StatusCode);

        var fresh = Codes(await Body(reissued));
        Assert.Equal(RecoveryCode.Count, fresh.Count);
        Assert.Empty(fresh.Intersect(old, StringComparer.Ordinal));

        using var another = AnOwner.AsABrowser(instance);
        using var refused = await another.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret, second_factor = old[0] },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task Turning_it_off_takes_the_codes_and_the_other_browsers_with_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (secret, _) = await OfferedAsync(client);
        await ConfirmedAsync(client, secret);

        using var elsewhere = await AnOwner.SignInAgainAsync(
            instance, TestContext.Current.CancellationToken, Next(secret));

        using var off = await client.PostAsJsonAsync(
            "/api/security/second-factor/off",
            new { password = AnOwner.Secret },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);

        var security = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);
        Assert.False(security!["second_factor_enabled"]!.GetValue<bool>());
        Assert.Equal(0, security["recovery_codes_remaining"]!.GetValue<int>());

        // A security change takes effect everywhere, and the browser that did
        // it stays signed in.
        using var theirs = await elsewhere.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, theirs.StatusCode);

        using var mine = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_signs_every_other_browser_out()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        using var elsewhere = await AnOwner.SignInAgainAsync(instance, TestContext.Current.CancellationToken);

        using var changed = await client.PostAsJsonAsync(
            "/api/security/password",
            new { current_password = AnOwner.Secret, password = "an altogether different password" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // The point of changing a password is that whoever else was in is out.
        using var theirs = await elsewhere.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, theirs.StatusCode);

        using var fresh = AnOwner.AsABrowser(instance);

        using var old = await fresh.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);

        using var now = await fresh.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = "an altogether different password" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, now.StatusCode);
    }

    [Fact]
    public async Task A_password_the_product_would_not_accept_is_refused_before_anything_changes()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var refused = await client.PostAsJsonAsync(
            "/api/security/password",
            new { current_password = AnOwner.Secret, password = "short" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("/problems/validation", Type(await Body(refused)));
    }

    [Fact]
    public async Task What_is_stored_of_the_codes_is_a_digest()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (secret, _) = await OfferedAsync(client);
        var codes = await ConfirmedAsync(client, secret);

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var stored = await context.RecoveryCodes.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RecoveryCode.Count, stored.Count);
        Assert.All(stored, code => Assert.Equal(32, code.CodeHash.Length));
        Assert.Contains(stored, code => code.CodeHash.SequenceEqual(RecoveryCode.Hash(codes[0])));

        // Nor does any of it reach the log.
        Assert.DoesNotContain(
            instance.Warnings, line => line.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(
            instance.Warnings, line => line.Contains(codes[0], StringComparison.Ordinal));
    }

    private static async Task<(string Secret, string Uri)> OfferedAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/security/second-factor",
            new { password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Body(response);

        return (body["secret"]!.GetValue<string>(), body["uri"]!.GetValue<string>());
    }

    private static async Task<IReadOnlyList<string>> ConfirmedAsync(HttpClient client, string secret)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/security/second-factor/confirm",
            new { code = Totp.CodeFor(secret, Totp.StepAt(DateTimeOffset.UtcNow)) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return Codes(await Body(response));
    }

    /// <summary>
    /// The next step's code. Enrolment spends the step it was confirmed with —
    /// which is the anti-replay rule doing its job — so the code that signs in
    /// afterwards is the one the phone shows thirty seconds later.
    /// </summary>
    private static string Next(string secret) =>
        Totp.CodeFor(secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1);

    private static IReadOnlyList<string> Codes(JsonNode body) =>
        [.. body["recovery_codes"]!.AsArray().Select(code => code!.GetValue<string>())];

    private static string Type(JsonNode body) => body["type"]!.GetValue<string>();

    private static async Task<JsonNode> Body(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
}
