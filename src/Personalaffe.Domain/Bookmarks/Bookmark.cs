namespace Personalaffe.Domain.Bookmarks;

/// <summary>A saved address whose identity survives edits and moves.</summary>
public sealed class Bookmark : IRecoverable
{
    private Bookmark() { }

    public Guid Id { get; private init; }
    public string Title { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset? ReadLaterAt { get; private set; }
    public string[] Tags { get; private set; } = [];
    public Guid? FolderId { get; private set; }
    public double? FavoritePosition { get; private set; }
    /// <summary>Privacy retained when the original folder no longer exists.</summary>
    public bool PrivateOrigin { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Actor? DeletedBy { get; set; }
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    public static Bookmark Make(string? title, string? url, string? description, Guid? folder, DateTimeOffset now, IEnumerable<string>? tags = null, bool readLater = false) => new()
    {
        Id = Guid.CreateVersion7(now), Title = BookmarkText.Title(title), Url = BookmarkText.Url(url),
        ReadLaterAt = readLater ? now : null, Description = BookmarkText.Description(description), Tags = BookmarkTags.Of(tags), FolderId = folder, CreatedAt = now, UpdatedAt = now,
    };

    public bool Change(string? title, string? url, string? description, Guid? folder, DateTimeOffset now, IEnumerable<string>? tags = null, bool? readLater = null)
    {
        var queued = readLater is null ? ReadLaterAt : readLater.Value ? ReadLaterAt ?? now : null;
        var wantedTags = tags is null ? Tags : BookmarkTags.Of(tags);
        var wantedTitle = BookmarkText.Title(title);
        var wantedUrl = BookmarkText.Url(url);
        var wantedDescription = BookmarkText.Description(description);
        if (Title == wantedTitle && Url == wantedUrl && Description == wantedDescription && FolderId == folder && Tags.SequenceEqual(wantedTags) && ReadLaterAt == queued)
            return false;
        ReadLaterAt = queued; Tags = wantedTags; Title = wantedTitle; Url = wantedUrl; Description = wantedDescription; FolderId = folder; UpdatedAt = now;
        return true;
    }

    public void FavoriteAt(double? position, DateTimeOffset now)
    {
        if (position.HasValue && !double.IsFinite(position.Value))
            throw Refusal.Validation("after", "A favorite position must be finite.");
        if (FavoritePosition == position) return;
        FavoritePosition = position; UpdatedAt = now;
    }

    public bool MarkForReading(bool later, DateTimeOffset now, DateTimeOffset? restoredAt = null)
    {
        if (restoredAt is { } restored && (restored < CreatedAt || restored > now))
            throw Refusal.Validation("queued_at", "A restored reading time must be between creation and now.");
        var wanted = later ? ReadLaterAt ?? restoredAt?.ToUniversalTime() ?? now : (DateTimeOffset?)null;
        if (ReadLaterAt == wanted) return false;
        ReadLaterAt = wanted; UpdatedAt = now; return true;
    }

    public void PreservePrivacy() => PrivateOrigin = true;
    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
