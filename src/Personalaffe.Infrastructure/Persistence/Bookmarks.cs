using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Infrastructure.Persistence;

public sealed class Bookmarks(PersonalaffeDbContext context, ICallerIdentity caller, TimeProvider clock, BookmarkSearch search) : IBookmarks
{
    private HashSet<Guid>? _privateFolders;

    public async Task<IReadOnlyList<Bookmark>> ListAsync(int offset, int limit, CancellationToken token, BookmarkFilter? filter = null)
    {
        if (filter is null)
            return await BookmarkVisibility.Bookmarks(context, caller.Caller).OrderByDescending(row => row.CreatedAt)
                .ThenBy(row => row.Id).Skip(offset).Take(limit).ToListAsync(token);
        var found = await search.FindAsync(filter, offset, limit, token);
        var ids = found.Select(row => row.Id).ToArray();
        var bookmarks = await BookmarkVisibility.Bookmarks(context, caller.Caller).Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, token);
        return [.. found.Where(row => bookmarks.ContainsKey(row.Id)).Select(row => bookmarks[row.Id])];
    }

    public async Task<IReadOnlyList<BookmarkFolder>> FoldersAsync(int offset, int limit, CancellationToken token) =>
        await BookmarkVisibility.Folders(context, caller.Caller).OrderBy(row => row.Name).ThenBy(row => row.Id).Skip(offset).Take(limit).ToListAsync(token);

    public Task<Bookmark?> FindAsync(Guid id, CancellationToken token) =>
        BookmarkVisibility.Bookmarks(context, caller.Caller, deleted: true).SingleOrDefaultAsync(row => row.Id == id, token);

    public Task<BookmarkFolder?> FindFolderAsync(Guid id, CancellationToken token) =>
        BookmarkVisibility.Folders(context, caller.Caller, deleted: true).SingleOrDefaultAsync(row => row.Id == id, token);

    public async Task<bool> PrivateAsync(Guid? folder, CancellationToken token)
    {
        if (folder is null) return false;
        _privateFolders ??= (await BookmarkVisibility.HiddenFolders(context).ToListAsync(token)).ToHashSet();
        return _privateFolders.Contains(folder.Value);
    }

    public async Task CheckPlacementAsync(Guid? moving, Guid? parent, CancellationToken token) =>
        BookmarkFolder.CheckPlacement(moving, parent, await context.BookmarkFolders.ToListAsync(token));

    public async Task<bool> FolderNameTakenAsync(string name, Guid? parent, Guid? except, CancellationToken token)
    {
        var names = await BookmarkVisibility.Folders(context, caller.Caller)
            .Where(row => row.ParentId == parent && row.Id != except).Select(row => row.Name).ToListAsync(token);
        return names.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public void Add(Bookmark bookmark) => context.Bookmarks.Add(bookmark);
    public void Add(BookmarkFolder folder) => context.BookmarkFolders.Add(folder);
    public async Task SaveAsync(CancellationToken token)
    {
        await GuardedSave.SaveAsync(context, "The saved link or folder", token);
        _privateFolders = null;
    }

    public async Task DeleteAsync(Bookmark bookmark, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        if (await PrivateAsync(bookmark.FolderId, token))
        {
            bookmark.PreservePrivacy();
        }

        bookmark.Delete(caller.Caller, now); bookmark.Touch(now);
        await SaveAsync(token);
    }

    public async Task DeleteAsync(BookmarkFolder folder, CancellationToken token)
    {
        var all = await context.BookmarkFolders.IgnoreQueryFilters().ToListAsync(token);
        var ids = Subtree(all, folder.Id).Select(row => row.Id).ToHashSet();
        var hidden = (await BookmarkVisibility.HiddenFolders(context).ToListAsync(token)).ToHashSet();
        // A public parent can contain separately private children. Deleting it
        // requires the explicit context too; otherwise invisible content changes.
        if (!caller.Caller.PrivateBookmarks && ids.Overlaps(hidden))
        {
            throw Refusal.Conflict("Enable private mode before changing this folder tree.");
        }

        var links = await context.Bookmarks.IgnoreQueryFilters()
            .Where(row => row.FolderId != null && ids.Contains(row.FolderId.Value)).ToListAsync(token);
        var now = clock.GetUtcNow();
        foreach (var row in all.Where(row => ids.Contains(row.Id)))
        {
            if (hidden.Contains(row.Id))
            {
                row.PreservePrivacy();
            }

            if (row.IsDeleted())
            {
                continue;
            }

            row.Delete(caller.Caller, now); row.Touch(now);
        }
        foreach (var row in links)
        {
            if (hidden.Contains(row.FolderId!.Value))
            {
                row.PreservePrivacy();
            }

            if (row.IsDeleted())
            {
                continue;
            }

            row.Delete(caller.Caller, now); row.Touch(now);
        }
        await SaveAsync(token);
    }

    internal static List<BookmarkFolder> Subtree(IReadOnlyList<BookmarkFolder> folders, Guid root)
    {
        var ids = new HashSet<Guid> { root };
        while (true)
        {
            var added = false;
            foreach (var folder in folders)
            {
                if (folder.ParentId is { } parent && ids.Contains(parent))
                {
                    added |= ids.Add(folder.Id);
                }
            }

            if (!added)
            {
                break;
            }
        }
        return [.. folders.Where(row => ids.Contains(row.Id))];
    }
}
