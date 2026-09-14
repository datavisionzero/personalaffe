using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>What one sweep did, per application.</summary>
public sealed record ThePurge(
    bool Swept, IReadOnlyList<PurgedFrom> Removed, IReadOnlyList<PurgeFailure> Failed)
{
    /// <summary>Somebody else was already sweeping; this instance did nothing.</summary>
    public static ThePurge Skipped { get; } = new(Swept: false, [], []);

    public int Total => Removed.Sum(removed => removed.Count);
}

/// <summary>How much went out of one application's Trash.</summary>
public sealed record PurgedFrom(WorkspaceApplication Application, int Count);

/// <summary>One application the sweep could not finish, and why.</summary>
public sealed record PurgeFailure(WorkspaceApplication Application, Exception Reason);

/// <summary>
/// Takes out of the Trash everything whose retention has run out.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It has no caller</strong> — no <c>ICallerIdentity</c>, no permission
/// check and nowhere to put one. This is the instance acting on a deadline the
/// owner set by deleting something a month ago, and an act that asked who was
/// calling would be an act that could be called.
/// </para>
/// <para>
/// <strong>It is stateless and catch-up safe.</strong> What it removes is
/// everything deleted before now minus the retention, not everything that
/// expired since the last sweep — so an instance that was down for a week
/// removes that week's worth on its next sweep and forgets nothing. Running it
/// twice removes nothing the second time; interrupting it leaves the rest for
/// an hour later.
/// </para>
/// <para>
/// <strong>It never asks whether an application is enabled.</strong> Disabling
/// an application (PERSONAL-E4) hides it from ordinary workflows; it does not
/// suspend a deadline, and it does not silently erase anything either. This act
/// is where that promise is kept, and there is no argument it could be passed
/// through.
/// </para>
/// </remarks>
public sealed class PurgeTheTrash(
    IEnumerable<ITrash> contributors,
    IExclusiveWork alone,
    RetentionSettings retention,
    TimeProvider clock)
{
    /// <summary>What the lock is called, so that two instances ask for the same one.</summary>
    public const string Work = "personalaffe.trash.purge";

    public async Task<ThePurge> ExecuteAsync(CancellationToken cancellationToken)
    {
        var expiredBefore = clock.GetUtcNow() - retention.Trash;
        var removed = new List<PurgedFrom>();
        var failed = new List<PurgeFailure>();

        var swept = await alone.TryAsync(
            Work,
            async token =>
            {
                foreach (var contributor in contributors)
                {
                    try
                    {
                        var count = await contributor.PurgeAsync(expiredBefore, token);

                        if (count > 0)
                        {
                            removed.Add(new PurgedFrom(contributor.Application, count));
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception reason)
                    {
                        // One module's inconsistency — a file whose bytes are
                        // already gone, a row something else is holding — must
                        // not be why the other three never get swept again. It
                        // is reported and the sweep carries on; the next one
                        // tries this application again from scratch.
                        failed.Add(new PurgeFailure(contributor.Application, reason));
                    }
                }
            },
            cancellationToken);

        return swept ? new ThePurge(Swept: true, removed, failed) : ThePurge.Skipped;
    }
}
