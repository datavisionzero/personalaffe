using Personalaffe.Application.Acts;
using Personalaffe.Application.Acts.Scratchpad;
using Personalaffe.Application.Ports;

namespace Personalaffe.Api.Hosting;

/// <summary>
/// The clock behind the two periods this instance keeps things for: a sweep on
/// start and one every hour, inside the application and nowhere else
/// (<c>docs/mvp-plan.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two sweeps, one loop.</strong> The Trash's purge and the Scratchpad's
/// expiry answer different questions and run on different periods, but they are
/// the same kind of work on the same clock and there is no reason for a second
/// timer. <strong>They are independent</strong>: one that throws is logged and
/// the other still runs, and the next hour happens anyway.
/// </para>
/// <para>
/// <strong>The interval is a constant and not a variable.</strong> An operator
/// has no reason to tune how often a sweep runs — what they might want to change
/// is how long things are kept, and that is
/// <see cref="RetentionSettings.Variable"/> and
/// <see cref="RetentionSettings.ScratchpadVariable"/>. One fewer knob is one
/// fewer thing an instance can be wrong about.
/// </para>
/// <para>
/// <strong>It sweeps at start</strong>, before the first tick, which is what
/// makes a week of downtime cost nothing: both sweeps work from a deadline and
/// not from what they did last, so the first one after an outage removes
/// everything that expired during it.
/// </para>
/// <para>
/// <strong>The deadlines are worked out here, once.</strong> Neither act is told
/// a retention, only a moment, so nothing downstream can come to its own
/// conclusion about when something expires.
/// </para>
/// <para>
/// A sweep that throws is logged and the next one happens anyway. These are the
/// only things in the product that destroy the owner's content without being
/// asked to; a background loop that dies quietly and leaves them growing forever
/// is the failure worth spending a try/catch on.
/// </para>
/// </remarks>
public sealed class RetentionService(
    IServiceScopeFactory scopeFactory,
    RetentionSettings retention,
    TimeProvider clock,
    ILogger<RetentionService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Trash retention is {Retention}; sweeping every {Interval}.",
            retention.Described(),
            Interval);

        logger.LogInformation(
            "The Scratchpad keeps an unpinned entry for {Retention} after it was last changed.",
            retention.DescribedScratchpad());

        using var timer = new PeriodicTimer(Interval, clock);

        do
        {
            await SweepTheTrashAsync(stoppingToken);
            await ExpireTheScratchpadAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task SweepTheTrashAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var purge = scope.ServiceProvider.GetRequiredService<PurgeTheTrash>();

            var swept = await purge.ExecuteAsync(stoppingToken);

            if (!swept.Swept)
            {
                logger.LogDebug("Another instance is sweeping the Trash; skipped this round.");
                return;
            }

            foreach (var failure in swept.Failed)
            {
                logger.LogError(
                    failure.Reason,
                    "Sweeping the {Application} Trash failed. The next sweep will try it again.",
                    failure.Application);
            }

            if (swept.Total == 0)
            {
                logger.LogDebug("Swept the Trash; nothing had expired.");
                return;
            }

            // Counts, per application. Never a name and never a word of what
            // the owner wrote: this line ends up in whatever the operator ships
            // their logs to.
            logger.LogInformation(
                "Swept the Trash: removed {Total} expired item(s), {Removed}.",
                swept.Total,
                string.Join(", ", swept.Removed.Select(from => $"{from.Application}: {from.Count}")));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The instance is stopping. Nothing to report and nothing half-done:
            // the sweep is safe to interrupt and safe to run again.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sweeping the Trash failed. The next sweep will try again.");
        }
    }

    private async Task ExpireTheScratchpadAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var expire = scope.ServiceProvider.GetRequiredService<ExpireTheEntries>();

            // The deadline, worked out here and nowhere else. It is the moment
            // an entry must have been changed before to be gone — so an entry
            // unpinned a minute ago has a whole period, whatever its capture
            // said, and switching the Scratchpad off changes nothing about it.
            var expiredBefore = clock.GetUtcNow() - retention.Scratchpad;

            var removed = await expire.ExecuteAsync(expiredBefore, stoppingToken);

            if (removed == 0)
            {
                logger.LogDebug("Swept the Scratchpad; nothing had expired.");
                return;
            }

            // A count and nothing else: not a word of what the owner wrote.
            logger.LogInformation("Swept the Scratchpad: removed {Removed} expired entry/entries.", removed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sweeping the Scratchpad failed. The next sweep will try again.");
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
