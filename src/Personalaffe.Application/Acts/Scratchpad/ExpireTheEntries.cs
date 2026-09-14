using Personalaffe.Application.Ports;

namespace Personalaffe.Application.Acts.Scratchpad;

/// <summary>
/// Destroys every unpinned entry whose period has run out, and says how many.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It has no caller and no enablement</strong> — no
/// <c>ICallerIdentity</c>, no permission check and nowhere to put one. This is
/// the instance acting on a period the operator configured, exactly as
/// <see cref="PurgeTheTrash"/> is, and an act that asked who was calling would
/// be an act that could be called. Switching the Scratchpad off hides it; it
/// does not suspend a deadline, and there is no argument here for it to be
/// passed through.
/// </para>
/// <para>
/// <strong>It takes the deadline rather than working one out.</strong> The
/// arithmetic is done once, in the service that runs it, so that nothing here
/// can come to its own conclusion about when something expires — and so a week
/// of downtime costs nothing: what goes is everything past the deadline, not
/// everything that expired since the last sweep. Running it twice removes
/// nothing the second time.
/// </para>
/// <para>
/// <strong>It takes no lock, where the Trash's sweep does.</strong> The purge
/// fans out over four modules and removes file bytes beside rows, which is work
/// two instances must not do at once; this is one statement against one table,
/// and a second instance running it at the same moment removes nothing and says
/// so. A lock would be a second thing that can be held when the instance dies.
/// </para>
/// </remarks>
public sealed class ExpireTheEntries(IScratchpadEntries entries)
{
    public Task<int> ExecuteAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken) =>
        entries.ExpireAsync(expiredBefore, cancellationToken);
}
