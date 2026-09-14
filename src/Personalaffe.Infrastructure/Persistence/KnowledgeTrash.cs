using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// Knowledge's half of the Trash (<see cref="ITrash"/>), and the second real
/// contributor after Files'.
/// </summary>
/// <remarks>
/// <para>
/// The rules are <see cref="Restoration"/>'s and nothing here invents any: a
/// deletion is one entry however much went with it, an ancestor in the Trash
/// comes back with what needs it, a title already taken is <c>conflict</c>, and
/// a place that has been removed for good puts the page at the top and says so.
/// Applying them to a second tree without changing them is what PERSONAL-E3 was
/// hoping for when it wrote them against nothing.
/// </para>
/// <para>
/// <strong>Revisions go wherever their page goes.</strong> They have no
/// <c>deleted_at</c> of their own — they are not content the owner deleted, they
/// are what a page used to say — so removing a page for good removes its
/// history in the same statement, and a page that comes back comes back with
/// all of it (<c>Domain/Revisions.cs</c>).
/// </para>
/// </remarks>
public sealed class KnowledgeTrash(
    PersonalaffeDbContext context, RetentionSettings retention) : ITrash
{
    public WorkspaceApplication Application => WorkspaceApplication.Knowledge;

    public async Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var all = await AllAsync(cancellationToken);

        var entries = all
            .Where(page => page.DeletedAt is not null && IsItsOwnEntry(all, page))
            .Select(page => new TrashEntry(
                WorkspaceApplication.Knowledge,
                page.Id,
                page.Title,
                page.ParentId is { } parent
                    ? all.FirstOrDefault(candidate => candidate.Id == parent)?.Title
                    : null,
                page.DeletedAt!.Value,
                page.DeletedBy!,
                page.DeletedAt!.Value + retention.Trash,
                page.UpdatedAt));

        return [.. entries.OrderByDescending(entry => entry.DeletedAt).ThenBy(entry => entry.Id).Take(limit)];
    }

    public async Task<RestoredTo?> RestoreAsync(
        Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken)
    {
        var all = await AllAsync(cancellationToken);

        if (all.FirstOrDefault(page => page.Id == id && page.DeletedAt is not null) is not { } itself)
        {
            return null;
        }

        RequireCurrent(itself.Version, held);

        var structure = Structure(all);
        var (above, reachesTheRoot) = Pages.Above(structure, itself.ParentId);
        var destination = reachesTheRoot ? itself.ParentId : null;

        var plan = Restoration.Plan(
            [new Placement(itself.Id, itself.Title, Deleted: true),
             .. above.Select(page => new Placement(page.Id, page.Title, page.Deleted))],
            reachesTheRoot,
            restoreAs,
            [.. all
                .Where(page => page.ParentId == destination && page.DeletedAt is null && page.Id != id)
                .Select(page => page.Title)]);

        var now = DateTimeOffset.UtcNow;
        var went = itself.DeletedAt;

        // Everything that went with it comes back with it. The moment is read
        // before anything is restored, because restoring clears it.
        foreach (var beneath in Pages.Subtree(structure, id)
                     .Select(one => all.First(page => page.Id == one.Id))
                     .Where(page => page.DeletedAt == went))
        {
            beneath.Restore();
            beneath.Touch(now);
        }

        // An ancestor comes back because the page would otherwise be
        // unreachable, and for no other reason: as itself, not with everything
        // it used to contain.
        foreach (var ancestor in all.Where(
                     page => page.Id != id && plan.Restore.Contains(page.Id)))
        {
            ancestor.Restore();
            ancestor.Touch(now);
        }

        // The title it comes back under and the place it comes back to, which
        // are what `Restoration` decided. The subtree loop above has already
        // taken it out of the Trash.
        itself.Write(plan.Name, plan.Parent, itself.Markdown, now);
        itself.Touch(now);

        await GuardedSave.SaveAsync(context, "The page", cancellationToken);

        return new RestoredTo(
            WorkspaceApplication.Knowledge,
            id,
            plan.Name,
            plan.Parent is { } parent ? all.First(page => page.Id == parent).Title : null,
            plan.MovedToTheRoot);
    }

    public async Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var all = await AllAsync(cancellationToken);

        if (all.FirstOrDefault(page => page.Id == id && page.DeletedAt is not null) is not { } itself)
        {
            return false;
        }

        RequireCurrent(itself.Version, held);

        // What goes is this Trash entry: the page, and what was deleted with it.
        // Something below it that the owner deleted separately is an entry of
        // its own, and destroying it as a side effect would destroy something
        // nobody selected.
        var going = Pages.Subtree(Structure(all), id)
            .Select(one => all.First(page => page.Id == one.Id))
            .Where(page => page.DeletedAt == itself.DeletedAt)
            .ToList();

        await RemoveAsync(going, cancellationToken);

        return true;
    }

    public async Task<int> EmptyAsync(CancellationToken cancellationToken) =>
        await RemoveAsync(
            [.. (await AllAsync(cancellationToken)).Where(page => page.DeletedAt is not null)],
            cancellationToken);

    public async Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken) =>
        await RemoveAsync(
            [.. (await AllAsync(cancellationToken))
                .Where(page => page.DeletedAt is { } at && at <= expiredBefore)],
            cancellationToken);

    /// <summary>
    /// Removes pages for good, and the history of each with it, and says how
    /// many pages went.
    /// </summary>
    private async Task<int> RemoveAsync(IReadOnlyList<Page> going, CancellationToken cancellationToken)
    {
        if (going.Count == 0)
        {
            return 0;
        }

        var ids = going.Select(page => page.Id).ToHashSet();

        // The history first, so that an interrupted removal leaves a page with
        // less history rather than a history with no page. Neither is good and
        // one of them is a row nothing can ever reach.
        await context.PageRevisions
            .Where(revision => ids.Contains(revision.PageId))
            .ExecuteDeleteAsync(cancellationToken);

        context.Pages.RemoveRange(going);
        await context.SaveChangesAsync(cancellationToken);

        return going.Count;
    }

    /// <summary>
    /// Every page there is, the Trash included. The bodies come with them here
    /// because these are the rows being written; every question that is only
    /// about the shape of the tree goes through <see cref="Structure"/>.
    /// </summary>
    private async Task<List<Page>> AllAsync(CancellationToken cancellationToken) =>
        await context.Pages.IgnoreQueryFilters().ToListAsync(cancellationToken);

    private static List<PageInTheTree> Structure(IReadOnlyList<Page> all) =>
    [
        .. all.Select(page => new PageInTheTree(
            page.Id, page.Title, page.ParentId, page.DeletedAt is not null, page.CreatedAt, page.UpdatedAt)),
    ];

    /// <summary>
    /// The check every guarded write in this module makes. It is here and not
    /// in <c>EntityTags</c> because that is the Api's, and Infrastructure does
    /// not know there is an HTTP header.
    /// </summary>
    private static void RequireCurrent(ContentVersion current, ContentVersion held)
    {
        if (!current.Matches(held))
        {
            throw Refusal.Stale(
                "The page has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.",
                current);
        }
    }

    /// <summary>
    /// Whether this is something the owner deleted rather than something that
    /// went with what they deleted.
    /// </summary>
    private static bool IsItsOwnEntry(IReadOnlyList<Page> all, Page page)
    {
        if (page.ParentId is not { } id)
        {
            return true;
        }

        var parent = all.FirstOrDefault(candidate => candidate.Id == id);

        // No parent left at all means the page it was under has been removed for
        // good while this was in the Trash: it is certainly nobody else's entry
        // now.
        return parent is null || parent.DeletedAt != page.DeletedAt;
    }
}
