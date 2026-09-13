using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Personalaffe.Api.Hosting;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The way back in when the password, the authenticator and the recovery codes
/// are all gone: the verb an operator runs on the machine, exercised against a
/// real database and then actually signed in with.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class OwnerRecoveryTests(PostgresFixture postgres)
{
    private const string NewPassword = "a password nobody has had before";

    [Fact]
    public async Task The_documented_steps_produce_a_password_that_signs_in()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        // Enrolled, so that recovery has the thing it is for to undo.
        var secret = await EnrolledAsync(client);

        var (code, output, _) = await RecoverAsync(instance.ConnectionString, NewPassword);

        Assert.Equal(0, code);
        Assert.Contains(AnOwner.Address, output, StringComparison.Ordinal);
        Assert.Contains("turned off", output, StringComparison.Ordinal);

        // The old session is gone, the old password is gone, and the second
        // factor is not asked for.
        using var before = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);

        using var fresh = AnOwner.AsABrowser(instance);

        using var old = await fresh.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = AnOwner.Secret },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);

        using var now = await fresh.PostAsJsonAsync(
            "/api/session",
            new { email = AnOwner.Address, password = NewPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, now.StatusCode);

        // And the authenticator that is in a river is not asked about.
        Assert.NotEmpty(secret);

        var security = await fresh.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);

        Assert.False(security!["second_factor_enabled"]!.GetValue<bool>());
        Assert.Equal(0, security["recovery_codes_remaining"]!.GetValue<int>());

        // A recovery nobody performed is a recovery somebody else performed, so
        // the owner is shown that one happened.
        Assert.NotNull(security["recovered_at"]);
    }

    [Fact]
    public async Task Nothing_else_about_the_instance_changes()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var granted = await client.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new
                {
                    scratchpad = "read_write",
                    knowledge = "none",
                    tasks = "none",
                    files = "none",
                },
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, granted.StatusCode);

        var token = JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!
            ["token"]!.GetValue<string>();

        var (code, _, _) = await RecoverAsync(instance.ConnectionString, NewPassword);
        Assert.Equal(0, code);

        // This is a way back in, not a reset: the agent the owner let in is
        // still let in.
        using var agent = instance.CreateClient();
        agent.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        using var me = await agent.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_password_the_product_would_not_accept_changes_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (code, _, complaint) = await RecoverAsync(instance.ConnectionString, "short");

        Assert.Equal(1, code);
        Assert.Contains("Nothing was changed", complaint, StringComparison.Ordinal);

        using var still = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task An_instance_nobody_has_claimed_has_no_owner_to_recover()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var started = instance.CreateClient();
        await started.GetAsync("/api/version", TestContext.Current.CancellationToken);

        var (code, _, complaint) = await RecoverAsync(instance.ConnectionString, NewPassword);

        Assert.Equal(1, code);
        Assert.Contains("no owner", complaint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_database_this_build_has_not_migrated_is_refused_before_anything_is_written()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var (code, _, complaint) = await RecoverAsync(connectionString, NewPassword);

        Assert.Equal(1, code);
        Assert.Contains("schema", complaint, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", complaint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("recover-owner")]
    [InlineData("recover-owner", "--password-file")]
    [InlineData("recover-owner", "--password", "hunter2")]
    [InlineData("recover-owner", "--password-file", "a", "b")]
    public async Task The_password_is_never_an_argument(params string[] args)
    {
        var complaint = new StringWriter();

        var code = await OwnerRecovery.RunAsync(
            args,
            new ConfigurationBuilder().Build(),
            new StringReader(string.Empty),
            new StringWriter(),
            complaint,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, code);
        Assert.Contains("--password-file", complaint.ToString(), StringComparison.Ordinal);
        Assert.Contains("shell history", complaint.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_new_password_reaches_no_output_and_no_row_in_the_clear()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var (_, output, complaint) = await RecoverAsync(instance.ConnectionString, NewPassword);

        Assert.DoesNotContain(NewPassword, output, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, complaint, StringComparison.Ordinal);

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("$argon2id$v=19$", owner.PasswordHash, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, owner.PasswordHash, StringComparison.Ordinal);
        Assert.Null(owner.TotpSecret);
        Assert.NotNull(owner.RecoveredAt);
    }

    /// <summary>The verb, run exactly as the procedure in docs/operations.md runs it.</summary>
    private static async Task<(int Code, string Output, string Complaint)> RecoverAsync(
        string connectionString, string password)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
            })
            .Build();

        var output = new StringWriter();
        var complaint = new StringWriter();

        var code = await OwnerRecovery.RunAsync(
            ["recover-owner", "--password-file", "-"],
            configuration,
            // On standard input, which is what `-` means and what the procedure
            // pipes into the container.
            new StringReader(password + "\n"),
            output,
            complaint,
            TestContext.Current.CancellationToken);

        return (code, output.ToString(), complaint.ToString());
    }

    private static async Task<string> EnrolledAsync(HttpClient client)
    {
        using var offered = await client.PostAsJsonAsync(
            "/api/security/second-factor",
            new { password = AnOwner.Secret },
            TestContext.Current.CancellationToken);

        var secret = JsonNode.Parse(
            await offered.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!
            ["secret"]!.GetValue<string>();

        using var confirmed = await client.PostAsJsonAsync(
            "/api/security/second-factor/confirm",
            new { code = Totp.CodeFor(secret, Totp.StepAt(DateTimeOffset.UtcNow)) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        return secret;
    }
}
