using System.Net;
using System.Security.Cryptography;
using System.Text;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Application.Acts.Bookmarks;

public sealed record BookmarkImportPreview(int ValidBookmarks, int Folders, int NewBookmarks, int NewFolders,
    int SkippedDuplicates, IReadOnlyList<BookmarkImportRejection> Rejected, string PreviewHash);
public sealed record BookmarkImportResult(int ImportedBookmarks, int CreatedFolders, int SkippedDuplicates,
    IReadOnlyList<BookmarkImportRejection> Rejected);
public sealed record BookmarkExport(string Html, int Bookmarks, int Folders, string Warning);

public sealed partial class BookmarkActs
{
    public const string HtmlWarning = "Browser HTML does not preserve private markings, favorites, tags or reading status and is not a backup. The file is unencrypted. Reimport private exports into a private folder.";

    private sealed record ImportPlan(BookmarkImportPreview Preview, List<BookmarkFolder> Folders, List<Bookmark> Bookmarks);

    public Task<BookmarkImportPreview> PreviewImportAsync(string? html, Guid? target, CancellationToken token) => Write(async ct =>
        (await PlanImport(html, target, ct)).Preview, token);

    public Task<BookmarkImportResult> ImportAsync(string? html, Guid? target, string? previewHash, CancellationToken token) => Write(async ct =>
    {
        var plan = await PlanImport(html, target, ct);
        if (string.IsNullOrWhiteSpace(previewHash) || !string.Equals(plan.Preview.PreviewHash, previewHash, StringComparison.Ordinal))
            throw Refusal.Conflict("The import or visible collection changed. Preview again before confirming.");
        foreach (var folder in plan.Folders) store.Add(folder);
        foreach (var bookmark in plan.Bookmarks) store.Add(bookmark);
        await store.SaveAsync(ct);
        return new BookmarkImportResult(plan.Bookmarks.Count, plan.Folders.Count, plan.Preview.SkippedDuplicates, plan.Preview.Rejected);
    }, token);

    private async Task<(List<BookmarkFolder> Folders, List<Bookmark> Bookmarks)> TransferCollection(CancellationToken token)
    {
        var folders = (await store.FoldersAsync(0, BookmarkTransferLimits.MaxCollectionEntries + 1, token)).ToList();
        var bookmarks = (await store.ListAsync(0, BookmarkTransferLimits.MaxCollectionEntries + 1, token)).ToList();
        if (folders.Count + bookmarks.Count > BookmarkTransferLimits.MaxCollectionEntries)
            throw Refusal.Validation("collection", "HTML transfer supports collections of at most 100000 visible entries.");
        return (folders, bookmarks);
    }

    private async Task<ImportPlan> PlanImport(string? html, Guid? target, CancellationToken token)
    {
        await Parent(target, token);
        var parsed = htmlParser.Parse(html ?? "");
        var collection = await TransferCollection(token);
        var fingerprint = new StringBuilder(html).Append('|').Append(target).Append('|').Append(caller.Caller.PrivateBookmarks);
        foreach (var row in collection.Folders.OrderBy(row => row.Id)) fingerprint.Append('|').Append(row.Id).Append(':').Append(row.UpdatedAt.UtcTicks);
        foreach (var row in collection.Bookmarks.OrderBy(row => row.Id)) fingerprint.Append('|').Append(row.Id).Append(':').Append(row.UpdatedAt.UtcTicks);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
        var folderMap = new Dictionary<int, Guid>();
        var folders = new List<BookmarkFolder>();
        var bookmarks = new List<Bookmark>();
        var keys = collection.Bookmarks.Select(row => (row.FolderId, BookmarkUrl.Key(row.Url))).ToHashSet();
        var byParent = new Dictionary<Guid, Dictionary<string, BookmarkFolder>>();
        void Index(BookmarkFolder row)
        {
            var key = row.ParentId ?? Guid.Empty;
            if (!byParent.TryGetValue(key, out var children)) byParent[key] = children = new(StringComparer.OrdinalIgnoreCase);
            children.TryAdd(row.Name, row);
        }
        foreach (var row in collection.Folders) Index(row);
        var duplicateCount = 0;
        var now = clock.GetUtcNow();
        foreach (var node in parsed.Nodes)
        {
            Guid? parent = node.Parent is { } index ? folderMap[index] : target;
            if (node.Url is null)
            {
                if (byParent.TryGetValue(parent ?? Guid.Empty, out var children) && children.TryGetValue(node.Title, out var existing)) { folderMap[node.Index] = existing.Id; continue; }
                var row = BookmarkFolder.Make(node.Title, parent, false, now);
                folders.Add(row); Index(row); folderMap[node.Index] = row.Id;
            }
            else
            {
                if (!keys.Add((parent, BookmarkUrl.Key(node.Url)))) { duplicateCount++; continue; }
                bookmarks.Add(Bookmark.Make(node.Title, node.Url, node.Description, parent, now));
            }
        }
        var all = collection.Folders.Concat(folders).ToList();
        if (folders.Count > 0) BookmarkFolder.CheckPlacement(folders[^1].Id, folders[^1].ParentId, all);
        return new(new(parsed.Nodes.Count(node => node.Url is not null), parsed.Nodes.Count(node => node.Url is null), bookmarks.Count, folders.Count, duplicateCount, parsed.Rejected, hash), folders, bookmarks);
    }

    public Task<BookmarkExport> ExportAsync(Guid? root, bool includePrivate, CancellationToken token) => Read(async ct =>
    {
        PrivateContext(includePrivate); await Parent(root, ct);
        var collection = await TransferCollection(ct);
        var folders = new List<BookmarkFolder>();
        var bookmarks = new List<Bookmark>();
        foreach (var folder in collection.Folders)
            if (includePrivate || !await store.PrivateAsync(folder.Id, ct)) folders.Add(folder);
        var allowedFolders = folders.Select(row => row.Id).ToHashSet();
        foreach (var bookmark in collection.Bookmarks)
            if ((includePrivate || !bookmark.PrivateOrigin) && (bookmark.FolderId is null || allowedFolders.Contains(bookmark.FolderId.Value))) bookmarks.Add(bookmark);
        if (root is { } rootId)
        {
            var included = new HashSet<Guid>();
            if (allowedFolders.Contains(rootId)) included.Add(rootId);
            for (var depth = 0; depth < BookmarkFolder.MaxDepth; depth++)
                foreach (var folder in folders) if (folder.ParentId is { } parent && included.Contains(parent)) included.Add(folder.Id);
            folders = folders.Where(row => included.Contains(row.Id)).ToList();
            bookmarks = bookmarks.Where(row => row.FolderId is { } parent && included.Contains(parent)).ToList();
        }
        if (folders.Count + bookmarks.Count > BookmarkTransferLimits.MaxEntries)
            throw Refusal.Validation("folder", "An HTML export supports at most 5000 entries; select a smaller folder.");
        var html = new StringBuilder("<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Bookmarks</TITLE>\n<H1>Bookmarks</H1>\n");
        var exported = folders.Select(row => row.Id).ToHashSet();
        void Level(Guid? parent, int depth)
        {
            if (depth > BookmarkFolder.MaxDepth) throw Refusal.Validation("folder", "Folder depth exceeds the HTML export limit.");
            html.AppendLine("<DL><p>");
            foreach (var folder in folders.Where(row => parent is null ? row.ParentId is null || !exported.Contains(row.ParentId.Value) : row.ParentId == parent).OrderBy(row => row.Name).ThenBy(row => row.Id))
            {
                html.Append("<DT><H3>").Append(WebUtility.HtmlEncode(folder.Name)).AppendLine("</H3>");
                Level(folder.Id, depth + 1); html.AppendLine("</DT>");
            }
            foreach (var bookmark in bookmarks.Where(row => parent is null ? row.FolderId is null || !exported.Contains(row.FolderId.Value) : row.FolderId == parent).OrderBy(row => row.Title).ThenBy(row => row.Id))
            {
                html.Append("<DT><A HREF=\"").Append(WebUtility.HtmlEncode(bookmark.Url)).Append("\">").Append(WebUtility.HtmlEncode(bookmark.Title)).AppendLine("</A></DT>");
                if (bookmark.Description.Length > 0) html.Append("<DD>").Append(WebUtility.HtmlEncode(bookmark.Description)).AppendLine("</DD>");
                if (html.Length > BookmarkTransferLimits.MaxBytes) throw Refusal.Validation("folder", "An HTML export supports at most 2 MiB; select a smaller folder.");
            }
            html.AppendLine("</DL><p>");
        }
        Level(null, 0);
        var result = html.ToString();
        if (Encoding.UTF8.GetByteCount(result) > BookmarkTransferLimits.MaxBytes) throw Refusal.Validation("folder", "An HTML export supports at most 2 MiB; select a smaller folder.");
        return new BookmarkExport(result, bookmarks.Count, folders.Count, HtmlWarning);
    }, token);
}
