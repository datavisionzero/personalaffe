using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;

namespace Personalaffe.Api.Hosting;

/// <summary>
/// The clock behind the Trash: a sweep on start and one every hour after it,
/// inside the application and nowhere else (<c>docs/mvp-plan.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The interval is a constant and not a variable.</strong> An operator
/// has no reason to tune how often a sweep runs — what they might want to
/// change is how long things are kept, and that is
/// <see cref="RetentionSettings.Variable"/>. One fewer knob is one fewer thing
/// an instance can be wrong about.
/// </para>
/// <para>
/// <strong>It sweeps at start</strong>, before the first tick, which is what
/// makes a week of downtime cost nothing: the sweep works from the deadline and
/// not from what it did last, so the first one after an outage removes
/// everything that expired during it.
/// </para>
/// <para>
/// A sweep that throws is logged and the next one happens anyway. The purge is
/// the only thing here that destroys the owner's content without being asked
/// to; a background loop that dies quietly and leaves the Trash growing forever
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

        using var timer = new PeriodicTimer(Interval, clock);

        do
        {
            await SweepAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
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
