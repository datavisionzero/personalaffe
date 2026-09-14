using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Scratchpad;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The clock behind the two periods this instance keeps things for: that it
/// runs inside the application, that it catches up after an outage, that two
/// instances do the Trash's work once, and what an unusable retention does to
/// the start.
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

        await using var instance = await AnInstance.StartedWithTrashAsync(postgres, knowledge);

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

        await using var instance = await AnInstance.StartedWithTrashAsync(
            postgres,
            new Dictionary<string, string?> { [RetentionSettings.Variable] = "1" },
            knowledge);

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
    public async Task An_unpinned_entry_past_the_deadline_goes_and_one_inside_it_stays()
    {
        var now = DateTimeOffset.UtcNow;
        var period = RetentionSettings.Default.Scratchpad;

        var connectionString = await SeededAsync(
            now,
            ScratchpadEntry.Capture("a second past the deadline", false, now - period - TimeSpan.FromSeconds(1)),
            ScratchpadEntry.Capture("a second inside it", false, now - period + TimeSpan.FromSeconds(1)),
            ScratchpadEntry.Capture("pinned a year ago", true, now.AddDays(-365)));

        await using var instance = Sweeping(connectionString, now);
        using var client = instance.CreateClient();
        using var ready = await client.GetAsync("/api/health/ready", Token);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        // The boundary is exact because the instance's clock is the one this
        // test placed the entries against, not because the test waited.
        var left = await SweptAsync(connectionString, 2);

        Assert.Equal(["a second inside it", "pinned a year ago"], left.Order(StringComparer.Ordinal));

        // And what it said about it: a count, and never a word of what the
        // owner wrote.
        Assert.Contains(
            instance.Logged,
            line => line.Contains("Swept the Scratchpad", StringComparison.Ordinal));
        Assert.DoesNotContain(
            instance.Logged,
            line => line.Contains("a second past the deadline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pinned_entry_survives_and_unpinning_it_starts_a_full_period()
    {
        var now = DateTimeOffset.UtcNow;
        var period = RetentionSettings.Default.Scratchpad;

        // Captured long ago, pinned, and let go a minute ago: the period runs
        // from the unpinning and not from the capture, so it is nowhere near
        // the deadline.
        var unpinned = ScratchpadEntry.Capture("pinned for a year", true, now.AddDays(-365));
        unpinned.Unpin(now.AddMinutes(-1));

        // The same entry, unpinned a full period and a minute ago: that one is
        // past the deadline and goes.
        var older = ScratchpadEntry.Capture("pinned, then let go", true, now.AddDays(-365));
        older.Unpin(now - period - TimeSpan.FromMinutes(1));

        var connectionString = await SeededAsync(
            now, unpinned, older, ScratchpadEntry.Capture("still pinned", true, now.AddDays(-365)));

        await using var instance = Sweeping(connectionString, now);
        using var client = instance.CreateClient();
        using var ready = await client.GetAsync("/api/health/ready", Token);

        var left = await SweptAsync(connectionString, 2);

        Assert.Equal(["pinned for a year", "still pinned"], left.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Switching_the_scratchpad_off_changes_nothing_about_its_sweep()
    {
        var now = DateTimeOffset.UtcNow;
        var period = RetentionSettings.Default.Scratchpad;

        var connectionString = await SeededAsync(
            now,
            ScratchpadEntry.Capture("expired while it was off", false, now - period - TimeSpan.FromDays(1)),
            ScratchpadEntry.Capture("not expired yet", false, now.AddHours(-1)));

        await SwitchedOffAsync(connectionString);

        await using var instance = Sweeping(connectionString, now);
        using var client = instance.CreateClient();
        using var ready = await client.GetAsync("/api/health/ready", Token);

        // Disabling hides an application; it does not suspend a deadline, and
        // switching it back on must not reveal something that should have gone
        // while it was off.
        var left = await SweptAsync(connectionString, 1);

        Assert.Equal(["not expired yet"], left);
    }

    [Fact]
    public async Task A_scratchpad_retention_the_instance_will_not_accept_stops_the_start()
    {
        await using var instance = AnInstance.Configured(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [RetentionSettings.ScratchpadVariable] = "a week" });

        await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() => instance.CreateClient(), Token));
    }

    [Fact]
    public async Task The_instance_says_both_periods_when_it_starts()
    {
        await using var instance = AnInstance.Configured(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?>
            {
                [RetentionSettings.Variable] = "45",
                [RetentionSettings.ScratchpadVariable] = "3",
            });

        using var client = instance.CreateClient();
        using var ready = await client.GetAsync("/api/health/ready", Token);

        // Two lines, one per period, so that an operator reading a container's
        // first second can see both numbers without asking. The value is
        // matched apart from the sentence because a structured sink renders a
        // property its own way.
        await Eventually(() => Said(instance, "Trash retention is", "45 days"));
        await Eventually(() => Said(instance, "The Scratchpad keeps an unpinned entry for", "3 days"));
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

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// A migrated database holding these entries, and nothing else.
    /// </summary>
    /// <remarks>
    /// Seeded before the instance starts, because the instance sweeps as soon
    /// as it is up: an entry written afterwards would be racing the thing under
    /// test.
    /// </remarks>
    private async Task<string> SeededAsync(DateTimeOffset now, params ScratchpadEntry[] entries)
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var context = AnInstance.ContextFor(connectionString);
        await AnInstance.MigratorFor(context).ApplyAsync(Token);

        context.ScratchpadEntries.AddRange(entries);
        await context.SaveChangesAsync(Token);

        return connectionString;
    }

    /// <summary>
    /// An instance whose clock is the one the entries were placed against, so
    /// that a boundary is a boundary rather than a margin somebody guessed.
    /// </summary>
    private static AnInstance Sweeping(string connectionString, DateTimeOffset now) =>
        AnInstance.AgainstWith(
            connectionString, services => services.AddSingleton<TimeProvider>(new Fixed(now)));

    /// <summary>What the sweep left, once it has left that many.</summary>
    private async Task<IReadOnlyList<string>> SweptAsync(string connectionString, int expected)
    {
        IReadOnlyList<string> left = [];

        await Eventually(async () =>
        {
            await using var context = AnInstance.ContextFor(connectionString);
            left = await context.ScratchpadEntries.Select(entry => entry.Text).ToListAsync(Token);

            return left.Count == expected;
        });

        return left;
    }

    private static bool Said(AnInstance instance, string sentence, string value) =>
        instance.Logged.Any(line =>
            line.Contains(sentence, StringComparison.Ordinal)
            && line.Contains(value, StringComparison.Ordinal));

    private static Task Eventually(Func<bool> settled) => Eventually(() => Task.FromResult(settled()));

    private static async Task Eventually(Func<Task<bool>> settled)
    {
        // Bounded, and polled rather than waited on: the sweep happens at
        // start, so this is a handful of iterations in practice and a failure
        // that says so rather than a hang.
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (await settled())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), Token);
        }

        Assert.Fail("The instance never got there.");
    }

    private async Task SwitchedOffAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);

        await using var command = new NpgsqlCommand(
            "update application set enabled = false where application = 'scratchpad'", connection);

        Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
    }
}
