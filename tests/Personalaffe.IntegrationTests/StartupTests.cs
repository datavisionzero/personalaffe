namespace Personalaffe.IntegrationTests;

/// <summary>
/// What happens when the environment is wrong: the instance does not start, and
/// it does not start halfway. The sentence an operator reads is checked where
/// it is written — <c>DatabaseSettingsTests</c> and <c>LogSettingsTests</c> in
/// the unit tests; what is checked here is that the host acts on it.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class StartupTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_instance_with_no_database_configured_never_builds_a_host()
    {
        await using var instance = AnInstance.Against(string.Empty);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("without ever building", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_instance_with_a_log_level_that_is_not_one_never_builds_a_host()
    {
        await using var instance = AnInstance.Configured(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { ["PERSONALAFFE_LOG_LEVEL"] = "chatty" });

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("without ever building", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_instance_that_started_once_starts_again_on_the_same_database()
    {
        await using var first = await AnInstance.StartedAsync(postgres);
        using (var client = first.CreateClient())
        {
            using var ready = await client.GetAsync("/api/health/ready", TestContext.Current.CancellationToken);
            ready.EnsureSuccessStatusCode();
        }

        await using var again = first.StartedAgain();
        using var restarted = again.CreateClient();

        using var response = await restarted.GetAsync("/api/health/ready", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
