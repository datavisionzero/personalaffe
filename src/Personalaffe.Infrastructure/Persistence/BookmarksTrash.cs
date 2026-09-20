using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Infrastructure.Persistence;

public sealed class BookmarksTrash(
    PersonalaffeDbContext context, ICallerIdentity caller, IBookmarkWork work, RetentionSettings retention, TimeProvider clock) : ITrash
{
    public Task<RestoredTo?> RestoreAsync(Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken) =>
        work.ExecuteAsync(ct => RestoreCoreAsync(id, held, restoreAs, ct), cancellationToken);

    public Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken) =>
        work.ExecuteAsync(ct => RemoveCoreAsync(id, held, ct), cancellationToken);

    public Task<int> EmptyAsync(CancellationToken cancellationToken) =>
        work.ExecuteAsync(ct => EmptyCoreAsync(ct), cancellationToken);

    public Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken) =>
        work.ExecuteAsync(ct => PurgeCoreAsync(expiredBefore, ct), cancellationToken);

    public WorkspaceApplication Application => WorkspaceApplication.Bookmarks;

    public Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken) =>
        work.ExecuteAsync(ct => ListCoreAsync(limit, ct), cancellationToken);

    private async Task<IReadOnlyList<TrashEntry>> ListCoreAsync(int limit, CancellationToken cancellationToken)
    {
        var (folders, bookmarks) = await EverythingAsync(cancellationToken);

        var entries = new List<TrashEntry>();

        foreach (var folder in folders.Where(folder => folder.IsDeleted() && IsItsOwnEntry(folders, folder)))
        {
            entries.Add(Entry(folder.Id, folder.Name, folder.ParentId, folders, folder));
        }

        foreach (var bookmark in bookmarks.Where(bookmark => bookmark.IsDeleted() && IsItsOwnEntry(folders, bookmark)))
        {
            entries.Add(Entry(bookmark.Id, bookmark.Title, bookmark.FolderId, folders, bookmark));
        }

        return
        [
            .. entries
                .OrderByDescending(entry => entry.DeletedAt)
                .ThenBy(entry => entry.Id)
                .Take(limit),
        ];
    }

    private async Task<RestoredTo?> RestoreCoreAsync(
        Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken)
    {
        var (folders, bookmarks) = await EverythingAsync(cancellationToken);

        var folder = folders.FirstOrDefault(candidate => candidate.Id == id && candidate.IsDeleted());
        var bookmark = bookmarks.FirstOrDefault(candidate => candidate.Id == id && candidate.IsDeleted());

        if (folder is null && bookmark is null)
        {
            return null;
        }

        var itself = new Placement(
            id,
            folder?.Name ?? bookmark!.Title,
            Deleted: true);

        var version = folder is not null ? folder.Version : bookmark!.Version;

        var where = folder is not null ? folder.ParentId : bookmark!.FolderId;

        RequireCurrent(version, held, folder is null ? "The bookmark" : "The folder");

        var (above, reachesTheRoot) = Above(folders, where);
        var destination = reachesTheRoot ? where : null;

        var plan = Restoration.Plan(
            [itself, .. above],
            reachesTheRoot,
            restoreAs,
            folder is null ? [] : [.. TakenIn(folders, destination, id)]);

        var now = clock.GetUtcNow();
        var went = folder is not null ? folder.DeletedAt : bookmark!.DeletedAt;

        if (folder is not null)
        {
            var (beneathFolders, beneathBookmarks) = WentWith(folders, bookmarks, folder.Id, went);

            foreach (var beneath in beneathFolders)
            {
                beneath.Restore();
                beneath.Touch(now);
            }

            foreach (var beneath in beneathBookmarks)
            {
                beneath.Restore();
                beneath.Touch(now);
            }
        }

        foreach (var ancestor in folders.Where(
                     candidate => candidate.Id != id && plan.Restore.Contains(candidate.Id)))
        {
            ancestor.Restore();
            ancestor.Touch(now);
        }

        if (folder is not null)
        {
            folder.Restore();
            folder.Change(plan.Name, plan.Parent, folder.Private, now);
            folder.Touch(now);
        }
        else if (bookmark is not null)
        {
            bookmark.Restore();
            bookmark.Change(plan.Name, bookmark.Url, bookmark.Description, plan.Parent, now);
            bookmark.Touch(now);
        }

        var restoredFolders = folders.Where(row => !row.IsDeleted() && row.UpdatedAt == now).ToArray();
        foreach (var restored in restoredFolders)
        {
            if (folders.Any(other => other.Id != restored.Id && !other.IsDeleted()
                && other.ParentId == restored.ParentId
                && string.Equals(other.Name, restored.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw Refusal.Conflict("A folder needed for restoration has a name already in use. Restore it under another name first.");
            }
        }
        var liveFolders = folders.Where(row => !row.IsDeleted()).ToArray();
        BookmarkFolder.CheckPlacement(folder?.Id, folder?.ParentId, liveFolders);

        await GuardedSave.SaveAsync(context, folder is null ? "The bookmark" : "The folder", cancellationToken);

        return new RestoredTo(
            WorkspaceApplication.Bookmarks,
            id,
            plan.Name,
            plan.Parent is { } parent
                ? folders.First(candidate => candidate.Id == parent).Name
                : null,
            plan.MovedToTheRoot);
    }

    private async Task<bool> RemoveCoreAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var (folders, bookmarks) = await EverythingAsync(cancellationToken);

        var folder = folders.FirstOrDefault(candidate => candidate.Id == id && candidate.IsDeleted());
        var bookmark = bookmarks.FirstOrDefault(candidate => candidate.Id == id && candidate.IsDeleted());

        if (folder is null && bookmark is null)
        {
            return false;
        }

        RequireCurrent(
            folder is not null ? folder.Version : bookmark!.Version,
            held,
            folder is null ? "The bookmark" : "The folder");

        if (bookmark is not null)
        {
            await RemoveAsync([bookmark], [], cancellationToken);
            return true;
        }

        var (goingFolders, goingBookmarks) = WentWith(folders, bookmarks, folder!.Id, folder.DeletedAt);

        await RemoveAsync(goingBookmarks, goingFolders, cancellationToken);

        return true;
    }

    private async Task<int> EmptyCoreAsync(CancellationToken cancellationToken)
    {
        var (folders, bookmarks) = await EverythingAsync(cancellationToken);

        return await RemoveAsync(
            [.. bookmarks.Where(bookmark => bookmark.IsDeleted())],
            [.. folders.Where(folder => folder.IsDeleted())],
            cancellationToken);
    }

    private async Task<int> PurgeCoreAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken)
    {
        var firstDay = BookmarkOpenDay.FirstDay(clock.GetUtcNow());
        await context.BookmarkOpenDays.Where(row => row.Day < firstDay).ExecuteDeleteAsync(cancellationToken);
        var folders = await context.BookmarkFolders.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var bookmarks = await context.Bookmarks.IgnoreQueryFilters().ToListAsync(cancellationToken);

        return await RemoveAsync(
            [.. bookmarks.Where(bookmark => bookmark.DeletedAt is { } at && at <= expiredBefore)],
            [.. folders.Where(folder => folder.DeletedAt is { } at && at <= expiredBefore)],
            cancellationToken);
    }

    private async Task<int> RemoveAsync(
        IReadOnlyList<Bookmark> bookmarks,
        IReadOnlyList<BookmarkFolder> folders,
        CancellationToken cancellationToken)
    {
        if (bookmarks.Count == 0 && folders.Count == 0)
        {
            return 0;
        }

        var hidden = (await BookmarkVisibility.HiddenFolders(context).ToListAsync(cancellationToken)).ToHashSet();
        var remainingFolders = await context.BookmarkFolders.IgnoreQueryFilters().Where(row => hidden.Contains(row.Id)).ToListAsync(cancellationToken);
        var remainingLinks = await context.Bookmarks.IgnoreQueryFilters().Where(row => row.FolderId != null && hidden.Contains(row.FolderId.Value)).ToListAsync(cancellationToken);
        foreach (var row in remainingFolders)
        {
            row.PreservePrivacy();
        }

        foreach (var row in remainingLinks)
        {
            row.PreservePrivacy();
        }

        context.Bookmarks.RemoveRange(bookmarks);
        context.BookmarkFolders.RemoveRange(folders);

        await context.SaveChangesAsync(cancellationToken);


        return bookmarks.Count + folders.Count;
    }

    private async Task<(List<BookmarkFolder> BookmarkFolders, List<Bookmark> Bookmarks)> EverythingAsync(
        CancellationToken cancellationToken) =>
        (await BookmarkVisibility.Folders(context, caller.Caller, deleted: true).ToListAsync(cancellationToken),
         await BookmarkVisibility.Bookmarks(context, caller.Caller, deleted: true).ToListAsync(cancellationToken));

    private static bool IsItsOwnEntry(IReadOnlyList<BookmarkFolder> folders, IRecoverable thing)
    {
        var parentId = thing switch
        {
            BookmarkFolder folder => folder.ParentId,
            Bookmark bookmark => bookmark.FolderId,
            _ => null,
        };

        if (parentId is not { } id)
        {
            return true;
        }

        var parent = folders.FirstOrDefault(candidate => candidate.Id == id);

        return parent is null || parent.DeletedAt != thing.DeletedAt;
    }

    private static (List<Placement> Chain, bool ReachesTheRoot) Above(
        IReadOnlyList<BookmarkFolder> folders, Guid? from)
    {
        var chain = new List<Placement>();
        var walking = from;

        while (walking is { } id)
        {
            if (folders.FirstOrDefault(candidate => candidate.Id == id) is not { } folder)
            {
                return (chain, false);
            }

            chain.Add(new Placement(folder.Id, folder.Name, folder.IsDeleted()));
            walking = folder.ParentId;
        }

        return (chain, true);
    }

    private static (List<BookmarkFolder> BookmarkFolders, List<Bookmark> Bookmarks) WentWith(
        IReadOnlyList<BookmarkFolder> folders,
        IReadOnlyList<Bookmark> bookmarks,
        Guid root,
        DateTimeOffset? went)
    {
        var subtree = Bookmarks.Subtree(folders, root);
        var ids = subtree.Select(folder => folder.Id).ToHashSet();

        return (
            [.. subtree.Where(folder => folder.DeletedAt == went)],
            [.. bookmarks.Where(bookmark => bookmark.FolderId is { } id && ids.Contains(id) && bookmark.DeletedAt == went)]);
    }

    private static IEnumerable<string> TakenIn(
        IReadOnlyList<BookmarkFolder> folders, Guid? folder, Guid itself) =>
        folders.Where(one => one.ParentId == folder && !one.IsDeleted() && one.Id != itself)
            .Select(one => one.Name);

    private TrashEntry Entry(
        Guid id, string name, Guid? where, IReadOnlyList<BookmarkFolder> folders, IRecoverable thing)
    {
        var deletedAt = thing.DeletedAt!.Value;

        return new TrashEntry(
            WorkspaceApplication.Bookmarks,
            id,
            name,
            where is { } parent
                ? folders.FirstOrDefault(candidate => candidate.Id == parent)?.Name
                : null,
            deletedAt,
            thing.DeletedBy!,
            deletedAt + retention.Trash,
            thing switch
            {
                BookmarkFolder folder => folder.UpdatedAt,
                Bookmark bookmark => bookmark.UpdatedAt,
                _ => deletedAt,
            });
    }

    private static void RequireCurrent(ContentVersion current, ContentVersion held, string what)
    {
        if (!current.Matches(held))
        {
            throw Refusal.Stale(
                $"{what} has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.",
                current);
        }
    }
}
