using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The one-time setup against a real database: that it works once, that the
/// schema is what stops it working twice, and that nothing it answers says who
/// the owner is.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SetupTests(PostgresFixture postgres)
{
    private const string Address = "owner@example.com";
    private const string Secret = "correct horse battery staple";

    [Fact]
    public async Task A_fresh_instance_says_it_needs_an_owner_and_says_nothing_else()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var response = await client.GetAsync("/api/setup", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();

        Assert.True(body["required"]!.GetValue<bool>());
        Assert.Equal(["required"], body.Select(member => member.Key));
    }

    [Fact]
    public async Task Setting_up_claims_the_instance_and_the_answer_changes()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var created = await client.PostAsJsonAsync(
            "/api/setup", new { email = Address, password = Secret }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

        // Every answer carries the version, the ones with no body included.
        Assert.True(created.Headers.Contains("Personalaffe-Version"));

        var state = await client.GetFromJsonAsync<JsonNode>(
            "/api/setup", TestContext.Current.CancellationToken);

        Assert.False(state!["required"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_instance_that_has_an_owner_cannot_acquire_a_second()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var first = await client.PostAsJsonAsync(
            "/api/setup", new { email = Address, password = Secret }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            "/api/setup",
            new { email = "someone-else@example.com", password = "another long enough password" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

        var problem = JsonNode.Parse(
            await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("/problems/conflict", problem["type"]!.GetValue<string>());
        Assert.DoesNotContain(Address, problem.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_callers_setting_up_at_once_produce_one_owner()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);

        // Eight clients, one instance, one unique index. Whichever of them wins,
        // the others are refused by the schema rather than by a read taken a
        // moment earlier.
        var attempts = Enumerable.Range(0, 8).Select(async which =>
        {
            using var client = instance.CreateClient();
            using var response = await client.PostAsJsonAsync(
                "/api/setup",
                new { email = $"owner{which}@example.com", password = Secret },
                TestContext.Current.CancellationToken);

            return response.StatusCode;
        });

        var answers = await Task.WhenAll(attempts);

        Assert.Single(answers, HttpStatusCode.NoContent);
        Assert.All(
            answers.Where(answer => answer != HttpStatusCode.NoContent),
            answer => Assert.Equal(HttpStatusCode.Conflict, answer));
    }

    [Theory]
    [InlineData("not an address", Secret, "email")]
    [InlineData(Address, "short", "password")]
    [InlineData("", "", "email")]
    public async Task A_field_that_is_wrong_is_named(string email, string password, string field)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/setup", new { email, password }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("/problems/validation", problem["type"]!.GetValue<string>());
        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task A_field_the_object_does_not_define_is_said_out_loud()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var content = new StringContent(
            $$"""{"email": "{{Address}}", "passwrd": "{{Secret}}"}""", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/setup", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        // Ignoring it silently is how a misspelled field becomes a setup with
        // no password and a person who cannot tell why.
        Assert.Equal("/problems/unknown-field", problem["type"]!.GetValue<string>());
        Assert.Equal("passwrd", problem["field"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_password_reaches_no_answer_and_no_log_line()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var created = await client.PostAsJsonAsync(
            "/api/setup", new { email = Address, password = Secret }, TestContext.Current.CancellationToken);

        Assert.Empty(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var refused = await client.PostAsJsonAsync(
            "/api/setup", new { email = Address, password = Secret }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            Secret,
            await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        Assert.DoesNotContain(instance.Warnings, line => line.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_is_stored_is_an_argon2id_value_and_never_the_password()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var created = await client.PostAsJsonAsync(
            "/api/setup", new { email = Address, password = Secret }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var owner = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            context.Owners, TestContext.Current.CancellationToken);

        Assert.Equal(Address, owner.Email);
        Assert.Equal(Address, owner.NormalizedEmail);
        Assert.StartsWith("$argon2id$v=19$", owner.PasswordHash, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, owner.PasswordHash, StringComparison.Ordinal);
    }
}
