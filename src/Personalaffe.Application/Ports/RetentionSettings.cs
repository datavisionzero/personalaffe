using System.Globalization;

namespace Personalaffe.Application.Ports;

/// <summary>
/// How long deleted lasting content stays recoverable
/// (<c>docs/operations.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// Thirty days by default, and the operator's to change for their instance. It
/// is not a setting in the workspace: an owner who wants something gone sooner
/// removes it from the Trash, which is a verb they already have, and one more
/// screen for a number almost nobody changes is one more thing to get wrong.
/// </para>
/// <para>
/// Days and not a duration spelling. A retention measured in hours is a Trash
/// that empties while somebody is at lunch, and one measured in years is a
/// backup by another name.
/// </para>
/// </remarks>
public sealed record RetentionSettings(TimeSpan Trash)
{
    public const string Variable = "PERSONALAFFE_TRASH_RETENTION";

    public const int DefaultDays = 30;

    public const int MinimumDays = 1;

    public const int MaximumDays = 3650;

    public static RetentionSettings Default { get; } = new(TimeSpan.FromDays(DefaultDays));

    public static RetentionSettings FromVariables(string? days)
    {
        var chosen = (days ?? string.Empty).Trim();

        if (chosen.Length == 0)
        {
            return Default;
        }

        if (!int.TryParse(chosen, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            throw new ArgumentException($"{Variable} is a whole number of days.");
        }

        if (whole is < MinimumDays or > MaximumDays)
        {
            throw new ArgumentException(
                $"{Variable} is between {MinimumDays} and {MaximumDays} days.");
        }

        return new RetentionSettings(TimeSpan.FromDays(whole));
    }

    /// <summary>How it reads in a log line and in an answer to an operator.</summary>
    public string Described() =>
        $"{Trash.TotalDays.ToString("0", CultureInfo.InvariantCulture)} days";
}
