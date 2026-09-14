using System.Globalization;

namespace Personalaffe.Domain.Knowledge;

/// <summary>
/// What a knowledge page is called: a label the owner reads and changes, and
/// never the page's identity.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A title is not an address.</strong> A page is found at its id, which
/// is made once and never changes, so renaming is free and no link anybody ever
/// wrote down breaks (<c>docs/mvp-plan.md</c>, PERSONAL-E7). That is the same
/// decision <see cref="Files.FileName"/> made, and it is worth making twice in
/// the same words because it is the decision both applications are built on.
/// </para>
/// <para>
/// <strong>Titles collide without regard to case among siblings.</strong> A
/// place in the tree holding <c>Architecture</c> and <c>architecture</c> is a
/// tree nobody can navigate, two pages nobody can tell apart in a sentence, and
/// two files with one name when the export writes them out.
/// </para>
/// </remarks>
public static class PageTitle
{
    /// <summary>
    /// How long a title is, in characters.
    /// </summary>
    /// <remarks>
    /// Characters and not bytes, where <see cref="Files.FileName"/> counts
    /// bytes, because the two limits are about different things: a file name
    /// has to fit in a filesystem's component, which is measured in bytes, and a
    /// title has to fit on a line somebody reads. Two hundred is a long
    /// sentence, and a title that is a long sentence is a page that wanted a
    /// heading.
    /// </remarks>
    public const int MaxLength = 200;

    /// <summary>How two titles are compared when one is in another's way.</summary>
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The title as it is stored: trimmed, and held to every rule.
    /// </summary>
    /// <exception cref="Refusal">
    /// <c>validation</c>: nothing, too long, or something that is a path rather
    /// than a title.
    /// </exception>
    public static string Accepted(string? title, string field = "title")
    {
        var kept = (title ?? string.Empty).Trim();

        if (kept.Length == 0)
        {
            throw Refusal.Validation(field, "A page has a title.");
        }

        if (kept.Length > MaxLength)
        {
            throw Refusal.Validation(
                field,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A title is at most {MaxLength} characters, and this one is {kept.Length}."));
        }

        // Not because anything here parses a title, but because two things
        // outside do: `pea knowledge` addresses a page by a path of titles, and
        // the export writes one file per page under the path its titles make.
        // A separator in a title would be a page the console cannot name and an
        // export that lands somewhere nobody asked for.
        if (kept.Contains('/', StringComparison.Ordinal) || kept.Contains('\\', StringComparison.Ordinal))
        {
            throw Refusal.Validation(
                field,
                "A title has no `/` or `\\` in it: the console addresses a page by a path of titles, and "
                + "the export writes one file per page.");
        }

        if (kept is "." or "..")
        {
            throw Refusal.Validation(field, "`.` and `..` mean something else everywhere. Pick another title.");
        }

        foreach (var character in kept)
        {
            if (char.IsControl(character))
            {
                throw Refusal.Validation(
                    field, "A title is one line. A newline in it is a title nobody can read back.");
            }
        }

        return kept;
    }

    /// <summary>Whether two titles are the same title.</summary>
    public static bool Same(string one, string other) => Comparer.Equals(one, other);
}
