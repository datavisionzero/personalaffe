using Personalaffe.Domain;

namespace Personalaffe.Application.Ports;

/// <summary>One thing the owner deleted, as the Trash shows it.</summary>
/// <remarks>
/// <see cref="UpdatedAt"/> is the version a restore or a permanent removal
/// sends back in <c>If-Match</c>: a list cannot answer an <c>ETag</c> per item,
/// so the value travels in the item (<c>docs/api.md</c>, The guarded write).
/// </remarks>
public sealed record TrashEntry(
    WorkspaceApplication Application,
    Guid Id,
    string Name,
    string? Where,
    DateTimeOffset DeletedAt,
    Actor DeletedBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset UpdatedAt);

/// <summary>Where a restored thing ended up.</summary>
/// <param name="Where">
/// The place it is in now, as the owner would recognise it, or nothing for the
/// root and for an application with no hierarchy.
/// </param>
/// <param name="MovedToTheRoot">
/// Whether the place it came from had already been removed for good, so that it
/// went to the root instead (<see cref="Restoration"/>). Nothing in this
/// product moves the owner's content without saying so.
/// </param>
public sealed record RestoredTo(
    WorkspaceApplication Application, Guid Id, string Name, string? Where, bool MovedToTheRoot);

/// <summary>
/// One application's half of the Trash: what of mine is in it, put this back,
/// remove this for good.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no central index.</strong> <c>deleted_at</c> lives in the
/// module's own table and this is the surface over it, because the alternative
/// — one table whose rows stand for rows in four others — is the generic
/// content entity PERSONAL-E3 rules out, and a second place that has to agree
/// with the first (<c>docs/codebase.md</c>).
/// </para>
/// <para>
/// An application whose module does not exist yet registers nothing and
/// contributes nothing; the Trash still answers. Appearing in it is the whole
/// of what a later content epic has to do.
/// </para>
/// <para>
/// Nothing here takes a caller. Permission is settled by the acts, once, so
/// that four implementations cannot come to four different conclusions about
/// what read access means.
/// </para>
/// </remarks>
public interface ITrash
{
    /// <summary>Which application's Trash this is.</summary>
    WorkspaceApplication Application { get; }

    /// <summary>
    /// What is in it, newest deletion first, at most <paramref name="limit"/>
    /// of them.
    /// </summary>
    Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Puts one back and says where it landed, or answers nothing if this
    /// application has no such entry.
    /// </summary>
    /// <exception cref="Refusal">
    /// <c>stale</c> if <paramref name="held"/> is not the entry's version,
    /// <c>conflict</c> if something now occupies the name it came back under.
    /// </exception>
    Task<RestoredTo?> RestoreAsync(
        Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken);

    /// <summary>
    /// Removes one for good — the row, and the bytes where there are any — or
    /// answers false if this application has no such entry.
    /// </summary>
    Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken);

    /// <summary>Removes everything in this application's Trash, and says how much.</summary>
    Task<int> EmptyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes everything deleted on or before <paramref name="expiredBefore"/>
    /// — the rows, and the bytes where there are any — and says how much.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The retention itself never reaches a contributor: the sweep does that
    /// arithmetic once, so four modules cannot come to four different answers
    /// about when something expires.
    /// </para>
    /// <para>
    /// <strong>It takes no caller and no enablement.</strong> The purge is the
    /// instance acting, not the owner or an agent, and disabling an application
    /// (PERSONAL-E4) must not suspend a retention deadline — there is no
    /// parameter here for it to be passed through.
    /// </para>
    /// </remarks>
    Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken);
}
