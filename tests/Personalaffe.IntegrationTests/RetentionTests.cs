using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The clock behind the Trash: that it runs inside the application, that it
/// catches up after an outage, that two instances do the work once, and what an
/// unusable retention does to the start.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RetentionTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Actor TheOwner = new() { Kind = CallerKind.Owner, Id = Guid.CreateVersion7() };

    [Fact]
    public async Task An_instance_sweeps_as_soon_as_it_is_up_and_removes_what_expired()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var now = DateTimeOffset.UtcNow;

        // One deleted long enough ago that it would have expired while the
        // instance was not running, and one deleted yesterday.
        var expired = knowledge.Holding("architecture", now.AddDays(-40), TheOwner);
        var kept = knowledge.Holding("groceries", now.AddDays(-1), TheOwner);

        await using var instance = await AnInstance.StartedWithAsync(
            postgres, services => services.AddSingleton<ITrash>(knowledge));

        using var client = instance.CreateClient();
        using var ready = await client.GetAsync("/api/health/ready", Token);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        var asked = await knowledge.Swept.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // The sweep works from the deadline and not from what it last did, so a
        // week of downtime costs nothing: what expired while nothing was
        // running goes on the first sweep after it.
        Assert.InRange(asked, now.AddDays(-30).AddSeconds(-30), now.AddDays(-30).AddSeconds(30));

        var left = await knowledge.ListAsync(10, Token);

        Assert.Equal([kept.Id], left.Select(entry => entry.Id));
        Assert.DoesNotContain(expired.Id, left.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_shorter_retention_moves_the_deadline_and_nothing_else()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var now = DateTimeOffset.UtcNow;

        var recent = knowledge.Holding("architecture", now.AddDays(-3), TheOwner);

        await using var instance = await AnInstance.StartedWithAsync(
            postgres,
            services => services.AddSingleton<ITrash>(knowledge),
            new Dictionary<string, string?> { [RetentionSettings.Variable] = "1" });

        using var client = instance.CreateClient();
        using var ready = await client.GetAsync("/api/health/ready", Token);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        await knowledge.Swept.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Assert.Empty(await knowledge.ListAsync(10, Token));
        Assert.DoesNotContain(recent.Id, (await knowledge.ListAsync(10, Token)).Select(entry => entry.Id));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("nightly")]
    public async Task A_retention_the_instance_will_not_accept_stops_the_start(string value)
    {
        await using var instance = AnInstance.Configured(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [RetentionSettings.Variable] = value });

        await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() => instance.CreateClient(), Token));
    }

    [Fact]
    public async Task Only_one_instance_sweeps_at_a_time()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var instance = AnInstance.Against(connectionString);
        using var client = instance.CreateClient();

        // Two callers on the same database, which is what two containers behind
        // one proxy are. The first holds the lock for as long as its work runs;
        // the second does not queue behind it, it skips this round.
        using var first = instance.Services.CreateScope();
        using var second = instance.Services.CreateScope();

        var holding = new TaskCompletionSource();
        var asked = new TaskCompletionSource<bool>();

        var held = first.ServiceProvider.GetRequiredService<IExclusiveWork>().TryAsync(
            "a-test", async _ =>
            {
                asked.SetResult(await second.ServiceProvider
                    .GetRequiredService<IExclusiveWork>()
                    .TryAsync("a-test", _ => Task.CompletedTask, Token));

                await holding.Task;
            },
            Token);

        Assert.False(await asked.Task.WaitAsync(TimeSpan.FromSeconds(30), Token));

        holding.SetResult();
        Assert.True(await held);

        // And the moment the first one lets go, the work is anybody's again.
        Assert.True(await second.ServiceProvider
            .GetRequiredService<IExclusiveWork>()
            .TryAsync("a-test", _ => Task.CompletedTask, Token));
    }

    [Fact]
    public async Task Two_pieces_of_work_do_not_wait_for_each_other()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var instance = AnInstance.Against(connectionString);
        using var client = instance.CreateClient();

        using var first = instance.Services.CreateScope();
        using var second = instance.Services.CreateScope();

        var holding = new TaskCompletionSource();
        var asked = new TaskCompletionSource<bool>();

        var held = first.ServiceProvider.GetRequiredService<IExclusiveWork>().TryAsync(
            "sweeping", async _ =>
            {
                asked.SetResult(await second.ServiceProvider
                    .GetRequiredService<IExclusiveWork>()
                    .TryAsync("something else", _ => Task.CompletedTask, Token));

                await holding.Task;
            },
            Token);

        Assert.True(await asked.Task.WaitAsync(TimeSpan.FromSeconds(30), Token));

        holding.SetResult();
        Assert.True(await held);
    }
}
