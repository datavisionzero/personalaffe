using Personalaffe.Application.Ports;
using Personalaffe.Domain.Weather;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// A weather provider that is a test's: it answers what it was told to and
/// counts how often it was asked.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No suite in this product reaches the internet.</strong> The real
/// provider is one adapter whose whole job is a request and a parse; what these
/// tests are about is everything around it — who may set a place, what a tile
/// says when there is no answer, whether a home page still draws. A test that
/// asked Open-Meteo would be a test that fails when somebody else's server has
/// a bad afternoon.
/// </para>
/// <para>
/// It is registered after the product's own, so the container's last
/// registration wins and nothing here has to unpick the composition root.
/// </para>
/// </remarks>
internal sealed class AFakeSky(Reading? reading = null) : IWeather
{
    public static readonly Reading Fine = new(
        Temperature: 16.1,
        FeelsLike: 16.5,
        High: 20.3,
        Low: 14.2,
        Wind: 2.2,
        Code: 3,
        Day: true,
        ReadAt: new DateTimeOffset(2026, 9, 16, 7, 15, 0, TimeSpan.Zero));

    public bool Available { get; set; } = true;

    public string Attribution => "Weather data by a test";

    /// <summary>How often anything asked this instance to look outside.</summary>
    public int Asked { get; private set; }

    /// <summary>How often anything asked it to turn a word into a point.</summary>
    public int LookedUp { get; private set; }

    /// <summary>What the last question was about.</summary>
    public WeatherPlace? LastPlace { get; private set; }

    public IReadOnlyList<SomewhereCalled> Places { get; set; } =
    [
        new("Wuppertal", "North Rhine-Westphalia", "Germany", 51.2563, 7.1482),
        new("Wupperthal", "Western Cape", "South Africa", -32.2761, 19.216),
    ];

    public Task<Reading?> ReadAsync(WeatherPlace place, CancellationToken cancellationToken)
    {
        Asked++;
        LastPlace = place;

        return Task.FromResult(Available && place.Configured ? reading : null);
    }

    public Task<IReadOnlyList<SomewhereCalled>> LookUpAsync(
        string query, CancellationToken cancellationToken)
    {
        LookedUp++;

        return Task.FromResult(Available ? Places : []);
    }
}
