using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Infrastructure.Persistence;

public sealed class BookmarkActivity(PersonalaffeDbContext context, ICallerIdentity caller) : IBookmarkActivity
{
    public async Task<IReadOnlyList<Bookmark>> FavoritesAsync(CancellationToken token) =>
        await BookmarkVisibility.Bookmarks(context, caller.Caller).Where(row => row.FavoritePosition != null)
            .OrderBy(row => row.FavoritePosition).ThenBy(row => row.Id).ToListAsync(token);

    public async Task<IReadOnlyList<Bookmark>> FrequentAsync(int limit, DateTimeOffset now, CancellationToken token)
    {
        var first = BookmarkOpenDay.FirstDay(now);
        var today = BookmarkOpenDay.DayOf(now);
        var totals = context.BookmarkOpenDays.Where(day => day.Day >= first && day.Day <= today)
            .GroupBy(day => day.BookmarkId)
            .Select(group => new { Id = group.Key, Count = group.Sum(day => day.Count), Last = group.Max(day => day.LastOpenedAt) });
        var visible = BookmarkVisibility.Bookmarks(context, caller.Caller).Where(row => row.FavoritePosition == null);
        return await visible.Join(totals, row => row.Id, total => total.Id, (row, total) => new { Row = row, total.Count, total.Last })
            .OrderByDescending(pair => pair.Count).ThenByDescending(pair => pair.Last).ThenBy(pair => pair.Row.Id)
            .Take(limit).Select(pair => pair.Row).ToListAsync(token);
    }

    public async Task OpenAsync(Guid bookmark, Guid eventId, DateTimeOffset now, CancellationToken token)
    {
        if (await context.BookmarkOpenings.AnyAsync(row => row.Id == eventId, token)) return;
        context.BookmarkOpenings.Add(new BookmarkOpening(eventId, bookmark));
        var today = BookmarkOpenDay.DayOf(now);
        var day = await context.BookmarkOpenDays.SingleOrDefaultAsync(row => row.BookmarkId == bookmark && row.Day == today, token);
        if (day is null) context.BookmarkOpenDays.Add(BookmarkOpenDay.First(bookmark, now));
        else day.Again(now);
        await context.SaveChangesAsync(token);
    }
}
