namespace Personalaffe.Domain.Bookmarks;

public static class BookmarkTags
{
    public const int MaxCount = 32;
    public const int MaxLength = 64;
    public static string[] Of(IEnumerable<string>? values)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var value in values ?? [])
        {
            if (++count > MaxCount) throw Refusal.Validation("tags", "Use at most 32 tags per bookmark or filter.");
            var name = (value ?? "").Trim().ToLowerInvariant();
            if (name.Length is 0 or > MaxLength || name.Any(char.IsControl))
                throw Refusal.Validation("tags", "A tag is one line of 1 to 64 characters.");
            result.Add(name);
        }
        return [.. result.Order(StringComparer.Ordinal)];
    }
}
