using Personalaffe.Domain.Search;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Application.Acts.Bookmarks;

public sealed record SavedBookmark(Bookmark Content, bool Private);
public sealed record SavedBookmarkFolder(BookmarkFolder Content, bool EffectivePrivate);
public sealed record SavedBookmarkFolders(IReadOnlyList<SavedBookmarkFolder> Items, int? NextOffset);
public sealed record SavedBookmarks(IReadOnlyList<SavedBookmark> Items, int? NextOffset);

/// <summary>Saved link operations share one transaction and visibility boundary.</summary>
public sealed partial class BookmarkActs(
    IBookmarks store, IBookmarkWork work, IBookmarkActivity activity, IBookmarkHtml htmlParser, ReachingAnApplication reaching,
    ICallerIdentity caller, RetentionSettings retention, TimeProvider clock)
{
    public Task<SavedBookmarks> ListAsync(int? offset, int? limit, CancellationToken token, string? q = null, Guid? folder = null, bool? favorites = null, bool? unsorted = null, string? sort = null, string[]? tags = null, bool? readLater = null) => Read(async ct =>
    {
        var skip = offset ?? 0;
        var take = limit ?? 100;
        if (skip < 0 || skip > int.MaxValue - 501) throw Refusal.Validation("offset", "Use a nonnegative offset.");
        if (take is < 1 or > 500) throw Refusal.Validation("limit", "Use a limit between 1 and 500.");
        var needle = string.IsNullOrWhiteSpace(q) ? null : Needle.Of(q);
        var order = sort ?? (readLater == true ? "reading" : needle is null ? "created" : "rank");
        if (order is not ("created" or "updated" or "title" or "rank" or "reading"))
            throw Refusal.Validation("sort", "Use created, updated, title, rank or reading.");
        if (folder is not null && unsorted == true) throw Refusal.Validation("folder", "Choose a folder or Unsorted.");
        await Parent(folder, ct);
        var found = await store.ListAsync(skip, take + 1, ct, new BookmarkFilter(needle, folder, favorites == true, unsorted == true, order, BookmarkTags.Of(tags), readLater == true));
        var rows = new List<SavedBookmark>();
        foreach (var row in found.Take(take)) rows.Add(await Of(row, ct));
        return new SavedBookmarks(rows, found.Count > take ? skip + take : null);
    }, token);

    public Task<SavedBookmarkFolders> FoldersAsync(int? offset, int? limit, CancellationToken token) => Read(async ct =>
    {
        var skip = offset ?? 0;
        var take = limit ?? 100;
        if (skip < 0 || skip > int.MaxValue - 501) throw Refusal.Validation("offset", "Use a nonnegative offset.");
        if (take is < 1 or > 500) throw Refusal.Validation("limit", "Use a limit between 1 and 500.");
        var found = await store.FoldersAsync(skip, take + 1, ct);
        var rows = new List<SavedBookmarkFolder>();
        foreach (var row in found.Take(take)) rows.Add(await Of(row, ct));
        return new SavedBookmarkFolders(rows, found.Count > take ? skip + take : null);
    }, token);

    public Task<IReadOnlyList<BookmarkTagCount>> TagsAsync(CancellationToken token) => Read(ct => store.TagsAsync(ct), token);

    public Task<SavedBookmark> ReadAsync(Guid id, CancellationToken token) => Read(async ct => await Of(await Find(id, ct), ct), token);
    public Task<SavedBookmarkFolder> ReadFolderAsync(Guid id, CancellationToken token) => Read(async ct => await Of(await Folder(id, ct), ct), token);

    public Task<SavedBookmark> CreateAsync(string? title, string? url, string? description, Guid? folder, CancellationToken token, string[]? tags = null, bool? readLater = null) => Write(async ct =>
    {
        await Parent(folder, ct);
        var row = Bookmark.Make(title, url, description, folder, clock.GetUtcNow(), tags, readLater == true);
        store.Add(row); await store.SaveAsync(ct);
        return await Of(row, ct);
    }, token);

    public Task<SavedBookmark> ChangeAsync(Guid id, string? title, string? url, string? description, Guid? folder, ContentVersion held, CancellationToken token, string[]? tags = null, bool? readLater = null) => Write(async ct =>
    {
        var row = await Find(id, ct); Current(row.Version, held);
        await Parent(folder, ct);
        if (row.Change(title, url, description, folder, clock.GetUtcNow(), tags, readLater)) await store.SaveAsync(ct);
        return await Of(row, ct);
    }, token);

    public Task<SavedBookmarkFolder> CreateFolderAsync(string? name, Guid? parent, bool isPrivate, CancellationToken token) => Write(async ct =>
    {
        PrivateContext(isPrivate); await Parent(parent, ct);
        await store.CheckPlacementAsync(null, parent, ct);
        await FolderName(name, parent, null, ct);
        var row = BookmarkFolder.Make(name, parent, isPrivate, clock.GetUtcNow());
        store.Add(row); await store.SaveAsync(ct);
        return await Of(row, ct);
    }, token);

    public Task<SavedBookmarkFolder> ChangeFolderAsync(Guid id, string? name, Guid? parent, bool isPrivate, ContentVersion held, CancellationToken token) => Write(async ct =>
    {
        var row = await Folder(id, ct); Current(row.Version, held);
        PrivateContext(isPrivate); await Parent(parent, ct);
        await store.CheckPlacementAsync(id, parent, ct);
        await FolderName(name, parent, id, ct);
        if (row.Change(name, parent, isPrivate, clock.GetUtcNow())) await store.SaveAsync(ct);
        return await Of(row, ct);
    }, token);

    public Task<bool> DeleteAsync(Guid id, ContentVersion held, CancellationToken token) => Write(async ct =>
    {
        var row = await Find(id, ct); Current(row.Version, held); await store.DeleteAsync(row, ct); return true;
    }, token);

    public Task<bool> DeleteFolderAsync(Guid id, ContentVersion held, CancellationToken token) => Write(async ct =>
    {
        var row = await Folder(id, ct); Current(row.Version, held); await store.DeleteAsync(row, ct); return true;
    }, token);

    private async Task<T> Read<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Bookmarks, token);
        return await work.ExecuteAsync(action, token);
    }
    private async Task<T> Write<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Bookmarks, token);
        return await work.ExecuteAsync(action, token);
    }
    private async Task<Bookmark> Find(Guid id, CancellationToken token)
    {
        var row = await store.FindAsync(id, token) ?? throw Refusal.NotFound("No bookmark at this address.");
        if (row.IsDeleted()) throw row.Gone("The bookmark", retention.Trash);
        return row;
    }
    private async Task<BookmarkFolder> Folder(Guid id, CancellationToken token)
    {
        var row = await store.FindFolderAsync(id, token) ?? throw Refusal.NotFound("No bookmark folder at this address.");
        if (row.IsDeleted()) throw row.Gone("The bookmark folder", retention.Trash);
        return row;
    }
    private async Task Parent(Guid? id, CancellationToken token) { if (id is { } parent) await Folder(parent, token); }
    private async Task<SavedBookmark> Of(Bookmark row, CancellationToken token) => new(row, caller.Caller.PrivateBookmarks && (row.PrivateOrigin || await store.PrivateAsync(row.FolderId, token)));
    private async Task<SavedBookmarkFolder> Of(BookmarkFolder row, CancellationToken token) => new(row, caller.Caller.PrivateBookmarks && await store.PrivateAsync(row.Id, token));
    private async Task FolderName(string? name, Guid? parent, Guid? except, CancellationToken token)
    {
        if (await store.FolderNameTakenAsync(BookmarkText.Title(name), parent, except, token))
            throw Refusal.Conflict("A visible folder already has that name here.");
    }
    private void PrivateContext(bool needed)
    {
        if (needed && !caller.Caller.PrivateBookmarks) throw Refusal.Validation("private", "Enable private mode to create or change private content.");
    }
    public static void Current(ContentVersion current, ContentVersion held)
    {
        if (!current.Matches(held)) throw Refusal.Stale("This saved link or folder has changed. Read it again before writing.", current);
    }
}
