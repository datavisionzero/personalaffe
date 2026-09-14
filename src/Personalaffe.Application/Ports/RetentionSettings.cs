using System.Globalization;

namespace Personalaffe.Application.Ports;

/// <summary>
/// The two periods this instance keeps things for
/// (<c>docs/operations.md</c>): how long deleted lasting content stays
/// recoverable, and how long an unpinned Scratchpad entry lasts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two periods and not one.</strong> Thirty days for what was deleted
/// and can be had back, seven for what was never meant to last. They answer
/// different questions — one is how long a mistake can be undone, the other is
/// how long a note is worth keeping — and a single number would have to be wrong
/// for one of them.
/// </para>
/// <para>
/// Both are the operator's to change for their instance and neither is a setting
/// in the workspace: an owner who wants something gone sooner deletes it, and
/// one who wants an entry kept pins it, and both of those are verbs they already
/// have. One more screen for a number almost nobody changes is one more thing to
/// get wrong.
/// </para>
/// <para>
/// Days and not a duration spelling. A retention measured in hours is a Trash
/// that empties while somebody is at lunch, and one measured in years is a
/// backup by another name.
/// </para>
/// </remarks>
public sealed record RetentionSettings(TimeSpan Trash, TimeSpan Scratchpad)
{
    public const string Variable = "PERSONALAFFE_TRASH_RETENTION";

    public const string ScratchpadVariable = "PERSONALAFFE_SCRATCHPAD_RETENTION";

    public const int DefaultDays = 30;

    public const int DefaultScratchpadDays = 7;

    public const int MinimumDays = 1;

    public const int MaximumDays = 3650;

    public static RetentionSettings Default { get; } =
        new(TimeSpan.FromDays(DefaultDays), TimeSpan.FromDays(DefaultScratchpadDays));

    public static RetentionSettings FromVariables(string? days, string? scratchpadDays = null) => new(
        Period(Variable, days, DefaultDays),
        Period(ScratchpadVariable, scratchpadDays, DefaultScratchpadDays));

    /// <summary>How the Trash's period reads in a log line and to an operator.</summary>
    public string Described() => Described(Trash);

    /// <summary>How the Scratchpad's period reads in the same places.</summary>
    public string DescribedScratchpad() => Described(Scratchpad);

    private static string Described(TimeSpan period) =>
        $"{period.TotalDays.ToString("0", CultureInfo.InvariantCulture)} days";

    private static TimeSpan Period(string variable, string? days, int fallback)
    {
        var chosen = (days ?? string.Empty).Trim();

        if (chosen.Length == 0)
        {
            return TimeSpan.FromDays(fallback);
        }

        if (!int.TryParse(chosen, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            throw new ArgumentException($"{variable} is a whole number of days.");
        }

        if (whole is < MinimumDays or > MaximumDays)
        {
            throw new ArgumentException(
                $"{variable} is between {MinimumDays} and {MaximumDays} days.");
        }

        return TimeSpan.FromDays(whole);
    }
}
