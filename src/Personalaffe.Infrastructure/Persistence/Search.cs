using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Search;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// One search over four tables, in Postgres (<see cref="ISearch"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The only hand-written SQL in the product.</strong> Everything else
/// this layer does is LINQ over the context; this is four statements written
/// out, because what they ask for has no LINQ spelling — <c>ts_rank</c> to
/// order by, <c>ts_headline</c> to quote the line the words were found in, and
/// a <c>tsquery</c> built from prefixes. Expressing that through a translator
/// would be a worse kind of obscure than a statement somebody can read and run
/// in <c>psql</c>.
/// </para>
/// <para>
/// <strong>The needle is a parameter and the words are already letters and
/// digits.</strong> <see cref="Needle"/> takes the caller's text apart before
/// it ever arrives here, so what this builds is <c>word:* &amp; word:*</c> out
/// of pieces that cannot carry an operator, and it is still passed as a
/// parameter rather than pasted in.
/// </para>
/// <para>
/// <strong>Hand-written SQL does not get the query filter, so every statement
/// says <c>deleted_at is null</c> itself.</strong> That is the one thing this
/// file has to keep true that the rest of the layer gets for free
/// (<c>RecoverableContent</c>), and it is what the epic's suite checks first:
/// deleting a page takes it out of the search in the same moment it takes it
/// out of everything else. The Scratchpad's statement has no such clause and
/// must not grow one — its entries are destroyed rather than set aside, and
/// the column does not exist.
/// </para>
/// <para>
/// <strong>One application per call, so four round trips for a whole-workspace
/// search.</strong> A union of four would be one, and would have to be built
/// out of whichever applications this caller may read — dynamic SQL, for a
/// workspace where the tables hold one person's notes and each statement is an
/// index lookup. Four small questions that are each obviously right is the
/// better trade here.
/// </para>
/// </remarks>
public sealed class Search(PersonalaffeDbContext context, BookmarkSearch bookmarks, IBookmarkWork bookmarkWork) : ISearch
{
    /// <summary>
    /// What <c>ts_headline</c> is asked for: one fragment, no markup, and
    /// enough words around the match to recognise the sentence.
    /// </summary>
    /// <remarks>
    /// <c>StartSel</c> and <c>StopSel</c> are quoted empty strings, which is
    /// how Postgres is told to mark nothing up — a bare <c>StartSel=</c> eats
    /// the option after it. A snippet leaves here as text, so nothing between
    /// here and a browser has to decide whether it is safe to render
    /// (<see cref="Found.Snippet"/>).
    /// </remarks>
    private const string Headline =
        "StartSel=\"\",StopSel=\"\",MaxWords=24,MinWords=8,ShortWord=2,MaxFragments=1";

    private const string Pages = $$"""
        SELECT p.id AS id,
               p.title AS title,
               nullif(ts_headline('simple', p.markdown, q.query, '{{Headline}}'), '') AS snippet,
               p.parent_id AS within,
               p.updated_at AS updated_at,
               ts_rank(p.search_vector, q.query) AS rank
        FROM pages AS p, to_tsquery('simple', {0}) AS q(query)
        WHERE p.deleted_at IS NULL AND p.search_vector @@ q.query
        ORDER BY rank DESC, p.updated_at DESC
        LIMIT {1}
        """;

    private const string Tasks = $$"""
        SELECT t.id AS id,
               t.title AS title,
               nullif(ts_headline('simple', t.description, q.query, '{{Headline}}'), '') AS snippet,
               t.list_id AS within,
               t.updated_at AS updated_at,
               ts_rank(t.search_vector, q.query) AS rank
        FROM tasks AS t, to_tsquery('simple', {0}) AS q(query)
        WHERE t.deleted_at IS NULL AND t.search_vector @@ q.query
        ORDER BY rank DESC, t.updated_at DESC
        LIMIT {1}
        """;

    /// <remarks>
    /// An entry has no title, so one is made: its first line, cut to something
    /// a row can draw. <c>chr(10)</c> rather than an escaped newline, so that
    /// the statement means the same whatever a session has
    /// <c>standard_conforming_strings</c> set to.
    /// </remarks>
    private const string Entries = $$"""
        SELECT e.id AS id,
               left(split_part(e.text, chr(10), 1), 120) AS title,
               nullif(ts_headline('simple', e.text, q.query, '{{Headline}}'), '') AS snippet,
               NULL::uuid AS within,
               e.updated_at AS updated_at,
               ts_rank(e.search_vector, q.query) AS rank
        FROM scratchpad_entries AS e, to_tsquery('simple', {0}) AS q(query)
        WHERE e.search_vector @@ q.query
        ORDER BY rank DESC, e.updated_at DESC
        LIMIT {1}
        """;

    /// <remarks>
    /// No snippet, because there is nothing to quote: a file's bytes are never
    /// read by this and never will be (VISION.md). The name is the whole of
    /// what was matched, and it is already in the row.
    /// </remarks>
    private const string Files = """
        SELECT f.id AS id,
               f.name AS title,
               NULL::text AS snippet,
               f.folder_id AS within,
               f.updated_at AS updated_at,
               ts_rank(f.search_vector, q.query) AS rank
        FROM files AS f, to_tsquery('simple', {0}) AS q(query)
        WHERE f.deleted_at IS NULL AND f.search_vector @@ q.query
        ORDER BY rank DESC, f.updated_at DESC
        LIMIT {1}
        """;

    public async Task<IReadOnlyList<Found>> FindAsync(
        WorkspaceApplication application,
        Needle needle,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(needle);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (application == WorkspaceApplication.Bookmarks)
            return await bookmarkWork.ExecuteAsync<IReadOnlyList<Found>>(async token =>
            {
                var found = await bookmarks.FindAsync(new BookmarkFilter(needle, null, false, false, "rank"), 0, limit, token);
                return [.. found.Select(row => new Found(application, row.Id, row.Title,
                    row.Snippet, row.FolderId, row.UpdatedAt, row.Rank, row.Url))];
            }, cancellationToken);

        var statement = application switch
        {
            WorkspaceApplication.Scratchpad => Entries,
            WorkspaceApplication.Knowledge => Pages,
            WorkspaceApplication.Tasks => Tasks,
            WorkspaceApplication.Files => Files,
            _ => throw new ArgumentOutOfRangeException(
                nameof(application), application, "An application with nothing to search."),
        };

        var rows = await context.Set<FoundRow>()
            .FromSqlRaw(statement, Query(needle), limit)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new Found(
                application, row.Id, row.Title, row.Snippet, row.Within, row.UpdatedAt, row.Rank)),
        ];
    }

    /// <summary>
    /// The needle as a <c>tsquery</c>: every word a prefix, all of them
    /// required (<see cref="Needle"/>).
    /// </summary>
    internal static string Query(Needle needle) => string.Join(
        " & ",
        (needle ?? throw new ArgumentNullException(nameof(needle)))
            .Words
            .Select(word => string.Create(CultureInfo.InvariantCulture, $"{word}:*")));
}

/// <summary>
/// One row of an answer to <see cref="Search"/>, and the shape all four of its
/// statements select.
/// </summary>
/// <remarks>
/// Keyless and mapped to no table: it is the shape of an answer, not a thing
/// that is stored, so nothing here is tracked and no migration creates
/// anything for it.
/// </remarks>
internal sealed class FoundRow
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Snippet { get; init; }

    public Guid? Within { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// What <c>ts_rank</c> answered. Postgres counts it in <c>real</c>, which
    /// is the precision an ordering needs and nothing is done to it here.
    /// </summary>
    public float Rank { get; init; }
}
