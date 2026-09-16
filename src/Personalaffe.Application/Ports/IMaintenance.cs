using Personalaffe.Domain;

namespace Personalaffe.Application.Ports;

/// <summary>
/// Whether this instance is being held still, and the two verbs that hold it
/// (<see cref="MaintenancePause"/>).
/// </summary>
/// <remarks>
/// <para>
/// One row, in the database, and deliberately not in the memory of the process
/// that begins the pause. An operator may well run two containers over one
/// database, and a pause one of them knew about would be a backup the other
/// wrote straight through.
/// </para>
/// <para>
/// <strong>It is read on every write and not cached.</strong> One indexed
/// single-row read against a database the request is about to write to anyway
/// is cheaper than any of the ways a cache goes wrong here: a stale "no" is a
/// write during a backup, which is the whole thing this exists to prevent.
/// </para>
/// </remarks>
public interface IMaintenance
{
    /// <summary>The pause, which exists from the first start and usually holds nothing.</summary>
    Task<MaintenancePause> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Writes back what <see cref="ReadAsync"/> handed out.</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
