using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Infrastructure.Bookmarks;

/// <summary>Parse inert Netscape bookmark HTML; no browsing context, loader or script engine.</summary>
public sealed class BookmarkHtml : IBookmarkHtml
{
    public ParsedBookmarkHtml Parse(string html)
    {
        if (Encoding.UTF8.GetByteCount(html) > BookmarkTransferLimits.MaxBytes)
            throw Refusal.Validation("html", "Bookmark HTML is limited to 2 MiB of UTF-8.");
        using var document = new HtmlParser().ParseDocument(html);
        foreach (var unsafeElement in document.QuerySelectorAll("script,style,iframe,object,embed,template")) unsafeElement.Remove();
        var nodes = new List<ImportedBookmarkNode>();
        var rejected = new List<BookmarkImportRejection>();
        var count = 0;
        void Level(IElement container, int? parent, int depth)
        {
            int? pendingFolder = null;
            int? lastBookmark = null;
            void Visit(IElement element, int wrapperDepth)
            {
                if (wrapperDepth > 128) throw Refusal.Validation("html", "HTML wrapper nesting exceeds 128 levels.");
                if (element.LocalName == "dl")
                {
                    var nested = pendingFolder ?? parent;
                    var nextDepth = depth + (pendingFolder is not null ? 1 : 0);
                    if (nextDepth > BookmarkFolder.MaxDepth) throw Refusal.Validation("html", "Bookmark folder depth exceeds 32 levels.");
                    Level(element, nested, nextDepth); pendingFolder = null; lastBookmark = null; return;
                }
                if (element.LocalName is "h3" or "a")
                {
                    if (++count > BookmarkTransferLimits.MaxEntries) throw Refusal.Validation("html", "Bookmark HTML is limited to 5000 entries including folders and rejected links.");
                    var isFolder = element.LocalName == "h3";
                    try
                    {
                        if (parent == -1) throw Refusal.Validation("html", "The containing folder was rejected.");
                        var url = isFolder ? null : BookmarkText.Url(element.GetAttribute("href"));
                        var text = element.TextContent.Trim();
                        var title = BookmarkText.Title(text.Length == 0 && url is not null ? new Uri(url).Host : text);
                        var index = nodes.Count;
                        nodes.Add(new(index, parent, title, url, ""));
                        if (isFolder) { pendingFolder = index; lastBookmark = null; }
                        else lastBookmark = index;
                    }
                    catch (Refusal error)
                    {
                        rejected.Add(new(count, error.Message));
                        if (isFolder) pendingFolder = -1;
                        lastBookmark = null;
                    }
                    return;
                }
                if (element.LocalName == "dd" && lastBookmark is { } last)
                {
                    try { nodes[last] = nodes[last] with { Description = BookmarkText.Description(element.TextContent) }; }
                    catch (Refusal error) { throw Refusal.Validation("html", $"Description for entry {last + 1}: {error.Message}"); }
                    return;
                }
                foreach (var child in element.Children) Visit(child, wrapperDepth + 1);
            }
            foreach (var child in container.Children) Visit(child, 0);
        }
        Level(document.DocumentElement, null, 0);
        if (count == 0) throw Refusal.Validation("html", "No bookmark links or folders were found in this HTML.");
        return new(nodes, rejected);
    }
}
