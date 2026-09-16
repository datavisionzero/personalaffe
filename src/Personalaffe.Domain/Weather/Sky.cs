namespace Personalaffe.Domain.Weather;

/// <summary>
/// What the sky is doing, as the World Meteorological Organization numbers it
/// and as a person reads it.
/// </summary>
/// <remarks>
/// <para>
/// The number is the provider's — WMO code 4677, which is what Open-Meteo and
/// most of its alternatives answer with — and the words are this product's, in
/// one place so that the browser and <c>pea</c> say the same thing. A client
/// that wanted its own words, or a picture, has the number.
/// </para>
/// <para>
/// A code nobody here has a word for is described as itself rather than
/// swallowed: a provider that adds one should show up as something odd on a
/// tile, not as a blank.
/// </para>
/// </remarks>
public static class Sky
{
    private static readonly Dictionary<int, string> Words = new()
    {
        [0] = "Clear",
        [1] = "Mainly clear",
        [2] = "Partly cloudy",
        [3] = "Overcast",
        [45] = "Fog",
        [48] = "Freezing fog",
        [51] = "Light drizzle",
        [53] = "Drizzle",
        [55] = "Heavy drizzle",
        [56] = "Light freezing drizzle",
        [57] = "Freezing drizzle",
        [61] = "Light rain",
        [63] = "Rain",
        [65] = "Heavy rain",
        [66] = "Light freezing rain",
        [67] = "Freezing rain",
        [71] = "Light snow",
        [73] = "Snow",
        [75] = "Heavy snow",
        [77] = "Snow grains",
        [80] = "Light showers",
        [81] = "Showers",
        [82] = "Heavy showers",
        [85] = "Light snow showers",
        [86] = "Snow showers",
        [95] = "Thunderstorm",
        [96] = "Thunderstorm with hail",
        [99] = "Thunderstorm with heavy hail",
    };

    /// <summary>The words for one code.</summary>
    public static string Described(int code) =>
        Words.TryGetValue(code, out var words) ? words : $"Weather code {code}";
}

/// <summary>
/// What it is doing at a <see cref="WeatherPlace"/>, once.
/// </summary>
/// <remarks>
/// <para>
/// A value and not a row: nothing here is stored. A reading is asked for, held
/// for as long as it is fresh, and thrown away — this instance is not a weather
/// archive and there is no table for one.
/// </para>
/// <para>
/// <see cref="ReadAt"/> is when this instance asked, not when the observation
/// was made. That is the number a tile needs to say how old what it is showing
/// is, which is the whole of making freshness clear
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E9).
/// </para>
/// </remarks>
/// <param name="High">The day's highest, where the provider gave one.</param>
/// <param name="Low">The day's lowest, where the provider gave one.</param>
/// <param name="Day">Whether the sun is up there, which is not the same as here.</param>
public sealed record Reading(
    double Temperature,
    double? FeelsLike,
    double? High,
    double? Low,
    double Wind,
    int Code,
    bool Day,
    DateTimeOffset ReadAt)
{
    /// <summary>The words for <see cref="Code"/>.</summary>
    public string Described => Sky.Described(Code);
}
