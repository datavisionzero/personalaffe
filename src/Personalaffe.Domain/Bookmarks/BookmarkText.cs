using System.Text;

namespace Personalaffe.Domain.Bookmarks;

/// <summary>Bounds on saved text; no linked resource is fetched.</summary>
public static class BookmarkText
{
    public const int MaxTitleLength = 200;
    public const int MaxUrlLength = 8192;
    public const int MaxDescriptionBytes = 65536;

    public static string Title(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length is 0 or > MaxTitleLength || text.Any(char.IsControl))
            throw Refusal.Validation("title", "A title is one line of 1 to 200 characters.");
        return text;
    }

    public static string Url(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length > MaxUrlLength || text.Any(char.IsControl)
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host) || text.Contains('\\'))
            throw Refusal.Validation("url", "Use an absolute HTTP or HTTPS address of at most 8192 characters.");
        return text;
    }

    public static string Description(string? value)
    {
        var text = value ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(text) > MaxDescriptionBytes)
            throw Refusal.Validation("description", "A description holds at most 65536 bytes of UTF-8.");
        return text;
    }
}
