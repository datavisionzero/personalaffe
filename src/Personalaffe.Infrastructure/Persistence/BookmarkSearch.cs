using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>Stored link text plus the current folder path, with visibility before ranking.</summary>
public sealed class BookmarkSearch(PersonalaffeDbContext context, ICallerIdentity caller)
{
    private const string Statement = $$"""
        WITH RECURSIVE {{BookmarkVisibility.HiddenFoldersCte}},
        paths(id, names, ancestry, depth) AS (
            SELECT id, name::text, ARRAY[id], 1 FROM bookmark_folders
            WHERE parent_id IS NULL AND deleted_at IS NULL
            UNION ALL
            SELECT child.id, parent.names || ' / ' || child.name, parent.ancestry || child.id, parent.depth + 1
            FROM bookmark_folders child JOIN paths parent ON child.parent_id = parent.id
            WHERE child.deleted_at IS NULL AND parent.depth < 32
        ), visible AS (
            SELECT b.*, p.names AS folder_path,
                b.search_vector || setweight(to_tsvector('simple', regexp_replace(coalesce(p.names, ''), '[^[:alnum:]]+', ' ', 'g')), 'C') AS words
            FROM bookmarks b LEFT JOIN paths p ON p.id = b.folder_id
            WHERE b.deleted_at IS NULL
              AND ({1} OR (NOT b.private_origin AND NOT EXISTS (SELECT 1 FROM hidden WHERE hidden.id = b.folder_id)))
              AND ({2} = '00000000-0000-0000-0000-000000000000'::uuid OR {2} = ANY(p.ancestry))
              AND (NOT {3} OR b.folder_id IS NULL)
              AND (NOT {4} OR b.favorite_position IS NOT NULL)
        )
        SELECT b.id AS "Id", b.title AS "Title", b.url AS "Url", b.folder_id AS "FolderId",
            b.updated_at AS "UpdatedAt", b.folder_path AS "FolderPath",
            coalesce(ts_rank(b.words, q.query), 0)::real AS "Rank",
            nullif(ts_headline('simple', b.description, q.query, 'StartSel="",StopSel="",MaxWords=24,MinWords=8,ShortWord=2,MaxFragments=1'), '') AS "Snippet"
        FROM visible b, to_tsquery('simple', nullif({0}, '')) AS q(query)
        WHERE {0} = '' OR b.words @@ q.query
        ORDER BY CASE WHEN {5} = 'title' THEN lower(b.title) END,
                 CASE WHEN {5} = 'rank' THEN ts_rank(b.words, q.query) END DESC,
                 CASE WHEN {5} IN ('rank', 'updated') THEN b.updated_at END DESC,
                 CASE WHEN {5} = 'created' THEN b.created_at END DESC, b.id
        LIMIT {6} OFFSET {7}
        """;

    public async Task<IReadOnlyList<BookmarkSearchRow>> FindAsync(BookmarkFilter filter, int offset, int limit, CancellationToken token)
    {
        caller.Caller.RequireRead(WorkspaceApplication.Bookmarks);
        var query = filter.Needle is null ? string.Empty : string.Join(" & ", filter.Needle.Words.Select(word => word + ":*"));
        return await context.Database.SqlQueryRaw<BookmarkSearchRow>(Statement, query, caller.Caller.PrivateBookmarks,
            filter.Folder ?? Guid.Empty, filter.Unsorted, filter.Favorites, filter.Sort, limit, offset).ToListAsync(token);
    }
}

public sealed class BookmarkSearchRow
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public Guid? FolderId { get; init; }
    public string? FolderPath { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public float Rank { get; init; }
    public string? Snippet { get; init; }
}
