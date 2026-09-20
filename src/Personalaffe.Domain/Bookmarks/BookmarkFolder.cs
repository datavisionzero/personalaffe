namespace Personalaffe.Domain.Bookmarks;

/// <summary>A container in the saved links' own tree.</summary>
public sealed class BookmarkFolder : IRecoverable
{
    public const int MaxDepth = 32;
    private BookmarkFolder() { }
    public Guid Id { get; private init; }
    public string Name { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public bool Private { get; private set; }
    public bool PrivateOrigin { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Actor? DeletedBy { get; set; }
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    public static BookmarkFolder Make(string? name, Guid? parent, bool isPrivate, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), Name = BookmarkText.Title(name), ParentId = parent,
        Private = isPrivate, CreatedAt = now, UpdatedAt = now,
    };

    public bool Change(string? name, Guid? parent, bool isPrivate, DateTimeOffset now)
    {
        var wanted = BookmarkText.Title(name);
        if (parent == Id) throw Refusal.Validation("parent", "A folder cannot contain itself.");
        if (Name == wanted && ParentId == parent && Private == isPrivate) return false;
        Name = wanted; ParentId = parent; Private = isPrivate; UpdatedAt = now;
        return true;
    }

    /// <summary>Checks a proposed move against the complete tree, including its descendants.</summary>
    public static void CheckPlacement(Guid? moving, Guid? parent, IReadOnlyCollection<BookmarkFolder> folders)
    {
        var tree = folders.ToDictionary(folder => folder.Id, folder => folder.ParentId);
        var id = moving ?? Guid.NewGuid();
        tree[id] = parent;
        foreach (var start in tree.Keys)
        {
            var seen = new HashSet<Guid>();
            Guid? cursor = start;
            while (cursor is { } current)
            {
                if (!seen.Add(current) || seen.Count > MaxDepth)
                    throw Refusal.Validation("parent", "A folder tree has no cycles and is at most 32 levels deep.");
                if (!tree.TryGetValue(current, out cursor))
                    throw Refusal.Validation("parent", "The parent folder does not exist.");
            }
        }
    }

    public void PreservePrivacy() => PrivateOrigin = true;
    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
