using Personalaffe.Domain.Scratchpad;

namespace Personalaffe.Application.Ports;

/// <summary>
/// Where Scratchpad entries are kept (<see cref="ScratchpadEntry"/>).
/// </summary>
/// <remarks>
/// <para>
/// One port for one store, and no Trash beside it. This is the application that
/// destroys what it deletes, so there is no <c>RestoreAsync</c> to leave
/// unimplemented and no <c>ITrash</c> to register — the absence is the design
/// and is asserted by a test.
/// </para>
/// <para>
/// Nothing here takes a caller. Permission and the application switch are
/// settled by the acts, once, through <c>ReachingAnApplication</c>, so that a
/// store cannot come to its own conclusion about what read access means.
/// </para>
/// </remarks>
public interface IScratchpadEntries
{
    /// <summary>
    /// The entries, newest capture first, at most <paramref name="limit"/> of
    /// them.
    /// </summary>
    Task<IReadOnlyList<ScratchpadEntry>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>One of them, or nothing at that id.</summary>
    Task<ScratchpadEntry?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Puts a new one down.</summary>
    Task AddAsync(ScratchpadEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the change an act has already made to a tracked entry, refusing as
    /// <c>stale</c> if the row moved underneath it.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>Destroys one for good. There is no way back from this.</summary>
    Task RemoveAsync(ScratchpadEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Destroys every unpinned entry last changed at or before
    /// <paramref name="expiredBefore"/>, and says how many.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It takes a deadline and nothing else</strong> — no caller, no
    /// enablement — exactly as <see cref="ITrash.PurgeAsync"/> does and for the
    /// same reason. The sweep is the instance acting on a period the operator
    /// configured, switching the Scratchpad off must not suspend it, and a
    /// parameter for either is a parameter somebody will eventually pass.
    /// </para>
    /// <para>
    /// The arithmetic that produces the deadline is done once, by the service
    /// that calls this, so that nothing downstream can come to its own
    /// conclusion about when something expires.
    /// </para>
    /// </remarks>
    Task<int> ExpireAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken);
}
