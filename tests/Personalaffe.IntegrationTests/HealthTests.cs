using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Liveness and readiness are not the same question
/// (<c>Http/HealthEndpoints.cs</c>), and this is where that is worth more than
/// the comment saying so.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class HealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_started_instance_is_live_and_ready()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var live = await client.GetAsync("/api/health/live", TestContext.Current.CancellationToken);
        using var ready = await client.GetAsync("/api/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        Assert.Equal(
            "live",
            (await live.Content.ReadFromJsonAsync<Health>(TestContext.Current.CancellationToken))?.Status);
        Assert.Equal(
            "ready",
            (await ready.Content.ReadFromJsonAsync<Health>(TestContext.Current.CancellationToken))?.Status);
    }

    [Fact]
    public async Task Readiness_fails_when_the_database_has_gone_away_and_liveness_does_not()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        // Warm the instance, so that what follows is a database that went away
        // rather than one that was never there.
        using (var before = await client.GetAsync("/api/health/ready", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        var database = new NpgsqlConnectionStringBuilder(instance.ConnectionString).Database!;
        await postgres.DropAsync(database);

        using var ready = await client.GetAsync("/api/health/ready", TestContext.Current.CancellationToken);
        using var live = await client.GetAsync("/api/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);

        // The point of the two being different questions: the process is fine,
        // and killing it would not bring the database back.
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task A_health_answer_carries_a_word_and_nothing_else()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        var body = await client.GetStringAsync("/api/health/ready", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("personalaffe_", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_says_why_to_the_operator_and_not_to_the_caller()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using (var before = await client.GetAsync("/api/health/ready", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        await postgres.DropAsync(new NpgsqlConnectionStringBuilder(instance.ConnectionString).Database!);

        using var ready = await client.GetAsync("/api/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);

        Assert.Contains(
            "Readiness could not be established.",
            instance.Warnings.Select(warning => warning.Split('\n')[0]));
    }

    private sealed record Health(string Status);
}
