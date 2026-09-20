namespace Personalaffe.Application.Ports;

public sealed record ImportedBookmarkNode(int Index, int? Parent, string Title, string? Url, string Description);
public sealed record BookmarkImportRejection(int Entry, string Reason);
public sealed record ParsedBookmarkHtml(IReadOnlyList<ImportedBookmarkNode> Nodes, IReadOnlyList<BookmarkImportRejection> Rejected);
public interface IBookmarkHtml
{
    ParsedBookmarkHtml Parse(string html);
}
public static class BookmarkTransferLimits
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxEntries = 5000;
    public const int MaxCollectionEntries = 100000;
}
