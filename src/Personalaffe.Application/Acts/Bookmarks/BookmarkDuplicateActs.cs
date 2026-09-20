using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Application.Acts.Bookmarks;

public sealed record BookmarkDuplicateGroup(string Url, IReadOnlyList<SavedBookmark> Items, int Count, int? NextMemberOffset);
public sealed record BookmarkDuplicates(IReadOnlyList<BookmarkDuplicateGroup> Groups, int? NextOffset);
public sealed record BookmarkDuplicateSelection(Guid Id, DateTimeOffset UpdatedAt);
public sealed record BookmarkCleanup(Guid Kept, IReadOnlyList<Guid> Removed);

public sealed partial class BookmarkActs
{
    public Task<BookmarkDuplicates> DuplicatesAsync(string? url, int? offset, int? limit, int? memberOffset, CancellationToken token) => Read(async ct =>
    {
        var skip = offset ?? 0; var take = limit ?? 20; var membersSkip = memberOffset ?? 0;
        if (skip < 0 || skip > int.MaxValue - 51 || take is < 1 or > 50 || membersSkip < 0 || membersSkip > int.MaxValue - 51)
            throw Refusal.Validation("pagination", "Use nonnegative offsets and a group limit of 1 to 50.");
        if (membersSkip > 0 && string.IsNullOrWhiteSpace(url)) throw Refusal.Validation("url", "Choose a URL before paging through its copies.");
        var key = string.IsNullOrWhiteSpace(url) ? null : BookmarkUrl.Key(url);
        var rows = await store.ListAsync(0, BookmarkTransferLimits.MaxCollectionEntries + 1, ct);
        if (rows.Count > BookmarkTransferLimits.MaxCollectionEntries)
            throw Refusal.Validation("collection", "Duplicate review supports at most 100000 visible bookmarks.");
        var groups = rows.GroupBy(row => BookmarkUrl.Key(row.Url), StringComparer.Ordinal)
            .Where(group => key is null ? group.Count() > 1 : group.Key == key)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Skip(skip).Take(take + 1).ToList();
        var result = new List<BookmarkDuplicateGroup>();
        foreach (var group in groups.Take(take))
        {
            var items = new List<SavedBookmark>();
            foreach (var row in group.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Skip(membersSkip).Take(50)) items.Add(await Of(row, ct));
            result.Add(new(group.Key, items, group.Count(), group.Count() > membersSkip + 50 ? membersSkip + 50 : null));
        }
        return new BookmarkDuplicates(result, groups.Count > take ? skip + take : null);
    }, token);

    public Task<BookmarkCleanup> CleanupAsync(Guid keep, IReadOnlyList<BookmarkDuplicateSelection>? remove, ContentVersion held, CancellationToken token) => Write(async ct =>
    {
        if (remove is null || remove.Count is < 1 or > 100 || remove.Any(row => row.Id == keep) || remove.Select(row => row.Id).Distinct().Count() != remove.Count)
            throw Refusal.Validation("remove", "Choose 1 to 100 distinct copies to remove, excluding the kept bookmark.");
        var kept = await Find(keep, ct); Current(kept.Version, held);
        var key = BookmarkUrl.Key(kept.Url);
        var removed = new List<Bookmark>();
        foreach (var selected in remove)
        {
            var row = await Find(selected.Id, ct); Current(row.Version, ContentVersion.Of(selected.UpdatedAt));
            if (BookmarkUrl.Key(row.Url) != key) throw Refusal.Conflict("A selected bookmark no longer has the same URL. Review the group again.");
            removed.Add(row);
        }
        // Every selection has passed visibility, identity and version checks before any write.
        foreach (var row in removed) await store.DeleteAsync(row, ct);
        return new BookmarkCleanup(kept.Id, removed.Select(row => row.Id).ToArray());
    }, token);
}
