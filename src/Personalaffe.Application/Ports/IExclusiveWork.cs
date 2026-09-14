namespace Personalaffe.Application.Ports;

/// <summary>
/// Work that must happen on one instance at a time, however many are running.
/// </summary>
/// <remarks>
/// <para>
/// An operator behind one proxy may well run two containers over one database,
/// and the purge is the one thing in this product that destroys content without
/// being asked to. Two of them sweeping at once is not dangerous so much as
/// pointless — the second finds nothing — but the log would say the work
/// happened twice and the deletions would race.
/// </para>
/// <para>
/// It is a try and not a wait: an instance that finds somebody else sweeping
/// skips this round and sweeps an hour later. Queueing behind a lock to do work
/// somebody has already done is a container holding a connection open for
/// nothing.
/// </para>
/// </remarks>
public interface IExclusiveWork
{
    /// <summary>
    /// Runs <paramref name="work"/> if no other instance is running the work
    /// called <paramref name="name"/>, and answers whether it did.
    /// </summary>
    Task<bool> TryAsync(string name, Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
