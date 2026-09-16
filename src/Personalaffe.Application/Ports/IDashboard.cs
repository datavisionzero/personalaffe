namespace Personalaffe.Application.Ports;

/// <summary>
/// One open task, as a tile draws it.
/// </summary>
/// <param name="List">
/// What the list it is in is called. A task on the home page is out of its
/// list, and "Buy tickets" without "Holiday" beside it is a line nobody can
/// place.
/// </param>
public sealed record OpenTask(
    Guid Id, string Title, Guid ListId, string List, DateOnly? DueOn, DateTimeOffset UpdatedAt);

/// <summary>One page, most recently written in first.</summary>
public sealed record RecentPage(Guid Id, string Title, Guid? ParentId, DateTimeOffset UpdatedAt);

/// <summary>
/// One Scratchpad entry, as a tile draws it.
/// </summary>
/// <param name="Text">
/// The whole of what it says. Cutting it to something a tile can draw is the
/// act's, because how much of it fits is not a fact about the database.
/// </param>
public sealed record RecentEntry(Guid Id, string Text, bool Pinned, DateTimeOffset UpdatedAt);

/// <summary>One file, most recently arrived first.</summary>
public sealed record RecentFile(
    Guid Id, string Name, Guid? FolderId, long Size, DateTimeOffset UpdatedAt);

/// <summary>
/// The few rows behind each tile of the home page
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E9).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One port over four applications, and not a fifth application.</strong>
/// The dashboard owns no content: every row it draws belongs to a module that
/// already has a store, and this is a reading of those tables in the one shape
/// a tile wants — a handful of rows, ordered by what makes them useful now.
/// Asking each module's own store instead would mean four list operations that
/// each answer a different question, paged and shaped for a screen that is not
/// this one.
/// </para>
/// <para>
/// <strong>Nothing here takes a caller.</strong> Permission and the switch are
/// settled by the act, once, through <c>ReachingAnApplication</c> — so a tile
/// nobody may see is never asked for, rather than asked for and thrown away.
/// What is in the Trash is left out by the query filter every one of these
/// reads inherits (<c>IRecoverable</c>).
/// </para>
/// </remarks>
public interface IDashboard
{
    /// <summary>
    /// What is open, soonest due first, then in the owner's own order. A task
    /// with no due date is not overdue and comes after the ones that are dated.
    /// </summary>
    Task<IReadOnlyList<OpenTask>> OpenTasksAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The pages most recently written in.</summary>
    Task<IReadOnlyList<RecentPage>> RecentPagesAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The entries most recently put down or changed.</summary>
    Task<IReadOnlyList<RecentEntry>> RecentEntriesAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The files most recently uploaded or changed.</summary>
    Task<IReadOnlyList<RecentFile>> RecentFilesAsync(int limit, CancellationToken cancellationToken);
}
