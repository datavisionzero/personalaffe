using System.Globalization;

namespace Personalaffe.Application.Ports;

/// <summary>
/// What the operator decides about the one outbound request this instance makes
/// (<c>docs/operations.md</c>): whether it is made at all, and how often.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Off is a real setting.</strong> A personal instance on a machine
/// that is not supposed to talk to anybody but its owner is a reasonable thing
/// to run, and "the weather tile is nice" is not a reason to take that away.
/// Switched off, nothing in this product opens a socket to anywhere.
/// </para>
/// <para>
/// <strong>Freshness is a period and not a cache size.</strong> There is one
/// place and one person; what matters is how often somebody else's servers are
/// troubled on their behalf. A quarter of an hour is finer than any forecast
/// changes and is well inside what Open-Meteo asks of a free user.
/// </para>
/// <para>
/// There is no provider setting, no URL and no key. The provider is a decision
/// this epic made once (<c>docs/adr/0009</c>); a configurable one would be a
/// second response shape to parse and a second set of terms to honour, for a
/// tile.
/// </para>
/// </remarks>
public sealed record WeatherSettings(bool Enabled, TimeSpan Freshness)
{
    public const string Variable = "PERSONALAFFE_WEATHER";

    public const string FreshnessVariable = "PERSONALAFFE_WEATHER_FRESHNESS";

    public const int DefaultFreshnessMinutes = 15;

    public const int MinimumFreshnessMinutes = 1;

    public const int MaximumFreshnessMinutes = 1440;

    public static WeatherSettings Default { get; } =
        new(true, TimeSpan.FromMinutes(DefaultFreshnessMinutes));

    public static WeatherSettings FromVariables(string? enabled, string? freshnessMinutes = null) =>
        new(Switched(enabled), Period(freshnessMinutes));

    /// <summary>How this reads in a log line and to an operator.</summary>
    public string Described() => Enabled
        ? $"every {Freshness.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)} minutes"
        : "switched off";

    private static bool Switched(string? enabled)
    {
        var chosen = (enabled ?? string.Empty).Trim();

        return chosen.ToLowerInvariant() switch
        {
            "" or "on" or "true" or "1" or "yes" => true,
            "off" or "false" or "0" or "no" => false,
            _ => throw new ArgumentException($"{Variable} is on or off."),
        };
    }

    private static TimeSpan Period(string? minutes)
    {
        var chosen = (minutes ?? string.Empty).Trim();

        if (chosen.Length == 0)
        {
            return TimeSpan.FromMinutes(DefaultFreshnessMinutes);
        }

        if (!int.TryParse(chosen, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            throw new ArgumentException($"{FreshnessVariable} is a whole number of minutes.");
        }

        if (whole is < MinimumFreshnessMinutes or > MaximumFreshnessMinutes)
        {
            throw new ArgumentException(
                $"{FreshnessVariable} is between {MinimumFreshnessMinutes} and "
                + $"{MaximumFreshnessMinutes} minutes.");
        }

        return TimeSpan.FromMinutes(whole);
    }
}
