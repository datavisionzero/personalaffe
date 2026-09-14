using System.Globalization;

namespace Personalaffe.Domain.Tasks;

/// <summary>
/// What a task or a list is called: one line, and what the owner would say out
/// loud if somebody asked what they had to do.
/// </summary>
/// <remarks>
/// It is one type for both because they are the same rule, and the one place
/// they differ — whether a name has to be unique among its siblings — is the
/// act's to decide and not the string's.
/// </remarks>
public static class TaskTitle
{
    /// <summary>How long a title is, in characters.</summary>
    /// <remarks>
    /// The same two hundred a knowledge page's title has, and for the same
    /// reason: it has to fit on a line somebody reads. A commitment that needs
    /// more than that has a description.
    /// </remarks>
    public const int MaxLength = 200;

    /// <summary>How two names are compared when one is in another's way.</summary>
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>The title as it is stored: trimmed, and held to every rule.</summary>
    /// <exception cref="Refusal"><c>validation</c>: nothing, too long, or more than one line.</exception>
    public static string Accepted(string? title, string field = "title")
    {
        var kept = (title ?? string.Empty).Trim();

        if (kept.Length == 0)
        {
            throw Refusal.Validation(field, "A task is something, and something has a name.");
        }

        if (kept.Length > MaxLength)
        {
            throw Refusal.Validation(
                field,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A title is at most {MaxLength} characters, and this one is {kept.Length}. "
                    + $"What does not fit is the description."));
        }

        foreach (var character in kept)
        {
            if (char.IsControl(character))
            {
                throw Refusal.Validation(
                    field, "A title is one line. What has a second line in it is a description.");
            }
        }

        return kept;
    }

    /// <summary>Whether two names are the same name.</summary>
    public static bool Same(string one, string other) => Comparer.Equals(one, other);
}
