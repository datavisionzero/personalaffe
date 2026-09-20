namespace Personalaffe.Domain.Bookmarks;

/// <summary>Only scheme, host and default port normalize; the original suffix remains exact.</summary>
public static class BookmarkUrl
{
    public static string Key(string value)
    {
        var text = BookmarkText.Url(value);
        var uri = new Uri(text);
        var authorityStart = text.IndexOf("://", StringComparison.Ordinal) + 3;
        if (authorityStart < 3) return text;
        var suffixStart = text.IndexOfAny(['/', '?', '#'], authorityStart);
        if (suffixStart < 0) suffixStart = text.Length;
        var authority = text[authorityStart..suffixStart];
        var userEnd = authority.LastIndexOf('@');
        var user = userEnd < 0 ? "" : authority[..(userEnd + 1)];
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6) host = "[" + host.Trim('[', ']') + "]";
        return uri.Scheme.ToLowerInvariant() + "://" + user + host
            + (uri.IsDefaultPort ? "" : ":" + uri.Port) + text[suffixStart..];
    }
}
