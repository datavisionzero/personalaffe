using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.Application.Acts.Knowledge;

/// <summary>The whole knowledge base, packaged.</summary>
public sealed record TheExport(string Name, byte[] Bytes);

/// <summary>
/// Writes every page out as Markdown in a zip, with enough structure beside it
/// to be useful outside personalaffe.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A zip of Markdown files, and not a format of our own.</strong> The
/// epic asks for "an open package that preserves enough hierarchy and reference
/// information for use outside personalaffe" (<c>docs/mvp-plan.md</c>), and the
/// test of that is what somebody can do with it having never heard of this
/// product: unzip it and read it. Every other shape — one big JSON document, a
/// SQL dump, a bespoke archive — fails that test on the first try.
/// </para>
/// <para>
/// <strong>The hierarchy is the directory layout</strong>, because that is the
/// form a person can see without being told anything. A page under
/// <c>Reisen › 2026</c> is at <c>pages/Reisen/2026/Bahn.md</c>, and a page with
/// children is both a file and a directory of the same name — which is how
/// every static site generator already does it.
/// </para>
/// <para>
/// <strong>What a directory layout cannot carry is in front matter.</strong> A
/// page's id is its identity and the thing every link names, and a path cannot
/// hold one; so each file opens with YAML naming the id, the parent's id, the
/// title as it really is, and the timestamps. YAML front matter because every
/// tool that reads Markdown in bulk already expects it, and because a person
/// looking at the top of the file can read it.
/// </para>
/// <para>
/// <strong>`knowledge.json` says the same thing in one place</strong>, for
/// whatever would rather not open three hundred files to find out the shape of
/// the tree. It is a convenience and never the only copy: deleting it loses
/// nothing that is not in the files.
/// </para>
/// <para>
/// <strong>It is built in memory and that is a decision.</strong> A personal
/// knowledge base is at most a few hundred pages of a mebibyte each and
/// compresses to very little; streaming a zip would buy nothing and would mean
/// an export that can fail halfway with a 200 already sent.
/// </para>
/// </remarks>
public sealed class ExportTheKnowledge(
    ReachingAnApplication reaching, IPages pages, TimeProvider clock)
{
    /// <summary>Where the pages are in the package.</summary>
    public const string Directory = "pages";

    /// <summary>What the index is called.</summary>
    public const string Index = "knowledge.json";

    public async Task<TheExport> ExecuteAsync(CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Knowledge, cancellationToken);

        var all = await pages.EverythingAsync(cancellationToken);
        var structure = await pages.TreeAsync(cancellationToken);
        var at = clock.GetUtcNow();
        var paths = PathsOf(all, structure);

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var page in all)
            {
                var entry = archive.CreateEntry($"{Directory}/{paths[page.Id]}.md", CompressionLevel.Optimal);

                await using var writing = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(Written(page));

                await writing.WriteAsync(bytes, cancellationToken);
            }

            var index = archive.CreateEntry(Index, CompressionLevel.Optimal);

            await using var writingIndex = index.Open();
            await JsonSerializer.SerializeAsync(
                writingIndex,
                new TheIndex(
                    at,
                    [.. all.Select(page => new TheIndexedPage(
                        page.Id,
                        page.Title,
                        page.ParentId,
                        $"{Directory}/{paths[page.Id]}.md",
                        page.CreatedAt,
                        page.UpdatedAt))]),
                IndexJson,
                cancellationToken);
        }

        return new TheExport(
            $"knowledge-{at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip",
            buffer.ToArray());
    }

    /// <summary>
    /// One page's file: YAML front matter, then the Markdown exactly as it was
    /// written.
    /// </summary>
    /// <remarks>
    /// The body is not touched. An export that reformatted, re-indented or
    /// re-wrapped anything would be an export of something slightly different
    /// from what the owner has.
    /// </remarks>
    internal static string Written(Page page)
    {
        var front = new StringBuilder()
            .Append("---\n")
            .Append("id: ").Append(page.Id.ToString("D", CultureInfo.InvariantCulture)).Append('\n')
            .Append("title: ").Append(Quoted(page.Title)).Append('\n')
            .Append("parent: ")
            .Append(page.ParentId?.ToString("D", CultureInfo.InvariantCulture) ?? "null")
            .Append('\n')
            .Append("created_at: ").Append(Moment(page.CreatedAt)).Append('\n')
            .Append("updated_at: ").Append(Moment(page.UpdatedAt)).Append('\n')
            .Append("---\n\n");

        return front.Append(page.Markdown).ToString();
    }

    /// <summary>
    /// Where each page goes in the package: the titles from the top down to it,
    /// made safe to write on any filesystem.
    /// </summary>
    /// <remarks>
    /// Titles already carry no separator (<see cref="PageTitle"/>), so the path
    /// is the titles joined. What is replaced here is the handful of characters
    /// Windows refuses in a name — a page called <c>Warum?</c> must still unzip
    /// there — and the replacement is only in the path: the title itself is in
    /// the front matter, unchanged. Two titles that come out the same after that
    /// are told apart by the first characters of the page's id, so no page ever
    /// overwrites another.
    /// </remarks>
    internal static Dictionary<Guid, string> PathsOf(
        IReadOnlyList<Page> all, IReadOnlyList<PageInTheTree> structure)
    {
        var titles = structure.ToDictionary(page => page.Id);
        var paths = new Dictionary<Guid, string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in all)
        {
            var parts = new List<string>();
            var walking = (Guid?)page.Id;

            while (walking is { } id && titles.TryGetValue(id, out var step) && parts.Count <= Page.MaxDepth)
            {
                parts.Insert(0, Safely(step.Title));
                walking = step.ParentId;
            }

            var path = string.Join('/', parts);

            if (!taken.Add(path))
            {
                path = $"{path} ({page.Id.ToString("N", CultureInfo.InvariantCulture)[..8]})";
                taken.Add(path);
            }

            paths[page.Id] = path;
        }

        return paths;
    }

    private static string Safely(string title)
    {
        var safe = new StringBuilder(title.Length);

        foreach (var character in title)
        {
            safe.Append(character is ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '-' : character);
        }

        // A trailing dot or space is a name Windows quietly drops, which would
        // make two pages one file.
        return safe.ToString().TrimEnd('.', ' ');
    }

    private static string Moment(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// A YAML scalar that cannot be read as anything else: double quoted, with
    /// the two characters that would end it escaped.
    /// </summary>
    private static string Quoted(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static readonly JsonSerializerOptions IndexJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    private sealed record TheIndex(DateTimeOffset ExportedAt, IReadOnlyList<TheIndexedPage> Pages);

    private sealed record TheIndexedPage(
        Guid Id,
        string Title,
        Guid? Parent,
        string File,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
