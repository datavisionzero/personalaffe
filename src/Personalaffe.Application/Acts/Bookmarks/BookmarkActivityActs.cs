using Personalaffe.Domain;
using Personalaffe.Domain.Tasks;

namespace Personalaffe.Application.Acts.Bookmarks;

public sealed record BookmarkDashboard(
    IReadOnlyList<SavedBookmark> Favorites, IReadOnlyList<SavedBookmark> Frequent,
    IReadOnlyList<SavedBookmark> Recent, bool HasMoreFavorites);

public sealed partial class BookmarkActs
{
    public Task<SavedBookmark> FavoriteAsync(Guid id, bool favorite, Guid? after, ContentVersion held, CancellationToken token) => Write(async ct =>
    {
        var row = await Find(id, ct);
        Current(row.Version, held);
        var now = clock.GetUtcNow();
        if (!favorite)
        {
            row.FavoriteAt(null, now);
        }
        else
        {
            if (after == id) throw Refusal.Validation("after", "A favorite cannot follow itself.");
            var others = (await activity.FavoritesAsync(ct)).Where(other => other.Id != id).ToList();
            var index = after is null ? -1 : others.FindIndex(other => other.Id == after);
            if (after is not null && index < 0) throw Refusal.NotFound("No visible favorite at that address.");
            double? above = index < 0 ? null : others[index].FavoritePosition;
            double? below = index + 1 >= others.Count ? null : others[index + 1].FavoritePosition;
            if (row.FavoritePosition is { } existing && (above is null || existing > above) && (below is null || existing < below))
                return await Of(row, ct);
            if (!Positions.RoomBetween(above ?? 0, below ?? ((above ?? 0) + 2 * Positions.Apart)))
            {
                var positions = Positions.Renumbered(others.Count);
                for (var i = 0; i < others.Count; i++) others[i].FavoriteAt(positions[i], now);
                above = index < 0 ? null : others[index].FavoritePosition;
                below = index + 1 >= others.Count ? null : others[index + 1].FavoritePosition;
            }
            row.FavoriteAt(Positions.Between(above, below), now);
        }
        await store.SaveAsync(ct);
        return await Of(row, ct);
    }, token);

    public Task<bool> OpenAsync(Guid id, Guid eventId, CancellationToken token) => Write(async ct =>
    {
        await Find(id, ct);
        if (eventId == Guid.Empty) throw Refusal.Validation("event_id", "An opening needs a unique event ID.");
        await activity.OpenAsync(id, eventId, clock.GetUtcNow(), ct);
        return true;
    }, token);

    public Task<IReadOnlyList<SavedBookmark>> HomeAsync(int limit, CancellationToken token) => Read<IReadOnlyList<SavedBookmark>>(async ct =>
    {
        var favorites = (await activity.FavoritesAsync(ct)).Take(limit).ToList();
        if (favorites.Count < limit)
            favorites.AddRange(await activity.FrequentAsync(limit - favorites.Count, clock.GetUtcNow(), ct));
        var rows = new List<SavedBookmark>();
        foreach (var row in favorites) rows.Add(await Of(row, ct));
        return rows;
    }, token);

    public Task<BookmarkDashboard> DashboardAsync(int? limit, CancellationToken token) => Read(async ct =>
    {
        var take = limit ?? 12;
        if (take is < 1 or > 100) throw Refusal.Validation("limit", "Use a limit between 1 and 100.");
        var favorites = await activity.FavoritesAsync(ct);
        var frequent = await activity.FrequentAsync(take, clock.GetUtcNow(), ct);
        var recent = await store.ListAsync(0, take, ct);
        async Task<IReadOnlyList<SavedBookmark>> Saved(IEnumerable<Domain.Bookmarks.Bookmark> rows)
        {
            var result = new List<SavedBookmark>();
            foreach (var row in rows) result.Add(await Of(row, ct));
            return result;
        }
        return new BookmarkDashboard(await Saved(favorites.Take(take)), await Saved(frequent), await Saved(recent), favorites.Count > take);
    }, token);
}
