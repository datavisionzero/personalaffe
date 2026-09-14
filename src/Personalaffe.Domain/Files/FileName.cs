using System.Globalization;
using System.Text;

namespace Personalaffe.Domain.Files;

/// <summary>
/// What a file or a folder is called: a label the owner reads, and never a path
/// the instance follows.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A name never reaches the filesystem.</strong> Bytes live at an
/// address derived from the file's id (<see cref="StorageAddress"/>), so
/// <c>../../etc/passwd</c> is not a traversal this has to defend against — it
/// is a name the owner cannot have because it is not a name, and the rules
/// below are about what the owner can navigate rather than about what the disk
/// would do with it. That order matters: a product whose safety rests on a
/// filter is a product one unfiltered path away from trouble.
/// </para>
/// <para>
/// <strong>Names collide without regard to case.</strong> One folder holding
/// <c>Notes</c> and <c>notes</c> is a tree nobody can navigate and two files
/// nobody can tell apart in a sentence; it is also what
/// <see cref="Restoration"/> already assumes when it says what is in the way.
/// The case the owner typed is kept and shown — it is theirs — and only the
/// comparison ignores it.
/// </para>
/// </remarks>
public static class FileName
{
    /// <summary>
    /// How long a name is, in bytes of UTF-8.
    /// </summary>
    /// <remarks>
    /// 255 is what every filesystem the owner will ever copy this to allows for
    /// one component, and nothing is gained by being the one place that allows
    /// more. Bytes rather than characters for the reason
    /// <c>ScratchpadEntry.MaxBytes</c> gives: a limit counted in characters
    /// means something different in German and in Japanese.
    /// </remarks>
    public const int MaxBytes = 255;

    /// <summary>How names are compared when one is in another's way.</summary>
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The name as it is stored: trimmed, and held to every rule.
    /// </summary>
    /// <param name="name">What the caller asked for.</param>
    /// <param name="field">
    /// What the refusal calls it — <c>name</c> almost everywhere, and whatever
    /// the endpoint's own field is called where it is not.
    /// </param>
    /// <exception cref="Refusal">
    /// <c>validation</c>: nothing, too long, or something that is a path rather
    /// than a name.
    /// </exception>
    public static string Accepted(string? name, string field = "name")
    {
        var kept = (name ?? string.Empty).Trim();

        if (kept.Length == 0)
        {
            throw Refusal.Validation(field, "A name is not empty.");
        }

        var bytes = Encoding.UTF8.GetByteCount(kept);

        if (bytes > MaxBytes)
        {
            throw Refusal.Validation(
                field,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A name is at most {MaxBytes} bytes of UTF-8, and this one is {bytes}."));
        }

        // `.` and `..` are the two names every filesystem has already taken.
        // They are refused here so that nothing downstream — an export, a
        // restore, a person reading a listing — has to wonder which folder is
        // meant.
        if (kept is "." or "..")
        {
            throw Refusal.Validation(field, "`.` and `..` mean something else everywhere. Pick another name.");
        }

        if (kept.Contains('/', StringComparison.Ordinal) || kept.Contains('\\', StringComparison.Ordinal))
        {
            throw Refusal.Validation(
                field,
                "A name is one name and not a path. Make the folders you want and put this in one of them.");
        }

        foreach (var character in kept)
        {
            if (char.IsControl(character))
            {
                throw Refusal.Validation(
                    field, "A name has no control characters in it. A newline in a file name is a file name nobody can type.");
            }
        }

        return kept;
    }

    /// <summary>Whether two names are the same name.</summary>
    public static bool Same(string one, string other) => Comparer.Equals(one, other);
}
