namespace Personalaffe.Domain.Bookmarks;

/// <summary>A compact idempotency receipt, removed with its bookmark.</summary>
public sealed record BookmarkOpening(Guid Id, Guid BookmarkId);

/// <summary>Explicit openings on one UTC calendar day, independent of content versions.</summary>
public sealed class BookmarkOpenDay
{
    private BookmarkOpenDay() { }
    public Guid BookmarkId { get; private init; }
    public DateOnly Day { get; private init; }
    public long Count { get; private set; }
    public DateTimeOffset LastOpenedAt { get; private set; }

    public static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(at.UtcDateTime);
    public static DateOnly FirstDay(DateTimeOffset now) => DayOf(now).AddDays(-29);
    public static BookmarkOpenDay First(Guid bookmark, DateTimeOffset now) => new()
    {
        BookmarkId = bookmark, Day = DayOf(now), Count = 1, LastOpenedAt = now.ToUniversalTime(),
    };
    public void Again(DateTimeOffset now)
    {
        Count = checked(Count + 1);
        if (now > LastOpenedAt) LastOpenedAt = now.ToUniversalTime();
    }
}
