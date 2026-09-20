using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Application.Ports;

public interface IBookmarkActivity
{
    Task<IReadOnlyList<Bookmark>> FavoritesAsync(CancellationToken token);
    Task<IReadOnlyList<Bookmark>> FrequentAsync(int limit, DateTimeOffset now, CancellationToken token);
    Task OpenAsync(Guid bookmark, Guid eventId, DateTimeOffset now, CancellationToken token);
}
