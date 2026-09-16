using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Weather;

namespace Personalaffe.Application.Acts.Weather;

/// <summary>
/// The weather tile's answer: where the owner said, what it is doing there, and
/// who to credit for saying so.
/// </summary>
/// <param name="Available">
/// Whether this instance asks anybody at all. An operator can switch the one
/// outbound request off (<see cref="WeatherSettings"/>), and then there is
/// nothing to wait for and the tile says so rather than looking broken.
/// </param>
/// <param name="Reading">
/// What it is doing, or nothing — no place set, nobody being asked, or a
/// provider that did not answer. The three are told apart by the fields beside
/// it, and none of them is a refusal.
/// </param>
public sealed record TheWeather(
    string? Place,
    double? Latitude,
    double? Longitude,
    WeatherUnits Units,
    DateTimeOffset UpdatedAt,
    bool Available,
    Reading? Reading,
    string Attribution)
{
    /// <summary>The version a guarded write of the place has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static TheWeather Of(WeatherPlace place, Reading? reading, IWeather provider) => new(
        place.Name,
        place.Latitude,
        place.Longitude,
        place.Units,
        place.UpdatedAt,
        provider.Available,
        reading,
        provider.Attribution);
}

/// <summary>
/// What it is doing where the owner said.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Its own act and its own address, on purpose.</strong> The dashboard
/// is one request over four applications; this is one request to somebody
/// else's server. Putting them together would mean a home page that cannot
/// finish drawing until a provider on the other side of the internet has
/// answered or timed out, which is the one thing PERSONAL-E9 says must not
/// happen — temporary unavailability is non-blocking, and the way to guarantee
/// that is structural.
/// </para>
/// <para>
/// <strong>Nothing here refuses because of the weather.</strong> No place, no
/// provider, no answer: all of them are a reading of nothing beside a place
/// that says why. A tile draws what it has.
/// </para>
/// </remarks>
public sealed class ReadTheWeather(IWeatherPlace places, IWeather provider)
{
    public async Task<TheWeather> ExecuteAsync(CancellationToken cancellationToken)
    {
        var place = await places.ReadAsync(cancellationToken);

        return TheWeather.Of(place, await provider.ReadAsync(place, cancellationToken), provider);
    }
}

/// <summary>
/// Says where the weather is for, or clears it. The owner's alone.
/// </summary>
/// <remarks>
/// The owner's, because it decides what this instance tells an outside service
/// about them: a point on the globe near where they live. That is not
/// somebody's agent's to set, and it is the same short list of things agent
/// access deliberately cannot reach as issuing a credential or switching an
/// application (<c>docs/mvp-plan.md</c>, PERSONAL-E2).
/// </remarks>
public sealed class SetTheWeatherPlace(
    ICallerIdentity caller, IWeatherPlace places, IWeather provider, TimeProvider clock)
{
    public async Task<TheWeather> ExecuteAsync(
        string? name,
        double? latitude,
        double? longitude,
        WeatherUnits units,
        ContentVersion held,
        CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("say where the weather is for");

        var place = await places.ReadAsync(cancellationToken);

        if (!place.Version.Matches(held))
        {
            throw Refusal.Stale(
                "The weather place has changed since it was read. Read it again: the write you sent "
                + "would have replaced somebody else's newer one.",
                place.Version);
        }

        if (place.Set(name, latitude, longitude, units, clock.GetUtcNow()))
        {
            await places.SaveAsync(cancellationToken);
        }

        // Read straight back, so that setting a place and seeing the weather
        // there is one request. It is the provider's cache that makes that
        // cheap rather than a second round of politeness.
        return TheWeather.Of(place, await provider.ReadAsync(place, cancellationToken), provider);
    }
}

/// <summary>
/// The places a geocoder thinks a word names. The owner's alone.
/// </summary>
/// <remarks>
/// <para>
/// The owner's, for the reason setting the place is: it is what makes this
/// instance send something of the owner's — a word they typed — to a service
/// outside it. A read-only agent that could ask this could use an instance as
/// somebody else's search engine.
/// </para>
/// <para>
/// It exists so that saying where the weather is for is typing "Wuppertal"
/// rather than looking two numbers up somewhere else. What it produces is
/// stored as a point (<see cref="WeatherPlace"/>), so the geocoder is asked
/// once and never again by a tile.
/// </para>
/// </remarks>
public sealed class LookUpAPlace(ICallerIdentity caller, IWeather provider)
{
    public async Task<IReadOnlyList<SomewhereCalled>> ExecuteAsync(
        string? query, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("look a place up");

        var asked = (query ?? string.Empty).Trim();

        if (asked.Length == 0)
        {
            throw Refusal.Validation("q", "Say what to look for.");
        }

        return await provider.LookUpAsync(asked, cancellationToken);
    }
}
