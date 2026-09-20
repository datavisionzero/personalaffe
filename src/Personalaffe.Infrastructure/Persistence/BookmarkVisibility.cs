using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>One SQL visibility predicate, applied before materialization and limits.</summary>
public static class BookmarkVisibility
{
    /// <summary>Deleted ancestors still carry privacy; missing ancestry fails closed.</summary>
    public static IQueryable<Guid> HiddenFolders(PersonalaffeDbContext context) =>
        context.Database.SqlQueryRaw<Guid>("""
            WITH RECURSIVE hidden(id) AS (
                SELECT f.id FROM bookmark_folders f
                WHERE f.private OR f.private_origin
                   OR (f.parent_id IS NOT NULL AND NOT EXISTS
                       (SELECT 1 FROM bookmark_folders p WHERE p.id = f.parent_id))
                UNION
                SELECT child.id FROM bookmark_folders child JOIN hidden parent ON child.parent_id = parent.id
            )
            SELECT id AS "Value" FROM hidden
            """);

    public static IQueryable<BookmarkFolder> Folders(PersonalaffeDbContext context, Caller caller, bool deleted = false)
    {
        caller.RequireRead(WorkspaceApplication.Bookmarks);
        var source = deleted ? context.BookmarkFolders.IgnoreQueryFilters() : context.BookmarkFolders;
        if (caller.PrivateBookmarks) return source;
        var hidden = HiddenFolders(context);
        return source.Where(folder => !hidden.Contains(folder.Id));
    }

    public static IQueryable<Bookmark> Bookmarks(PersonalaffeDbContext context, Caller caller, bool deleted = false)
    {
        caller.RequireRead(WorkspaceApplication.Bookmarks);
        var source = deleted ? context.Bookmarks.IgnoreQueryFilters() : context.Bookmarks;
        if (caller.PrivateBookmarks) return source;
        var hidden = HiddenFolders(context);
        return source.Where(bookmark => !bookmark.PrivateOrigin &&
            (bookmark.FolderId == null || (!hidden.Contains(bookmark.FolderId.Value)
                && context.BookmarkFolders.IgnoreQueryFilters().Any(folder => folder.Id == bookmark.FolderId))));
    }
}
