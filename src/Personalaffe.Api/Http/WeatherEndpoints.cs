using Personalaffe.Application.Acts.Weather;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Weather;

namespace Personalaffe.Api.Http;

/// <summary>What it is doing, once.</summary>
/// <param name="ReadAt">
/// When this instance asked, not when the observation was made. It is what lets
/// a tile say how old what it is showing is.
/// </param>
public sealed record ReadingResponse(
    double Temperature,
    double? FeelsLike,
    double? High,
    double? Low,
    double Wind,
    int Code,
    string Description,
    bool Day,
    DateTimeOffset ReadAt)
{
    public static ReadingResponse Of(Reading reading) => new(
        reading.Temperature,
        reading.FeelsLike,
        reading.High,
        reading.Low,
        reading.Wind,
        reading.Code,
        reading.Described,
        reading.Day,
        reading.ReadAt);
}

/// <summary>
/// The weather tile's answer: where the owner said, what it is doing there, and
/// who to credit.
/// </summary>
/// <param name="Available">
/// Whether this instance asks anybody at all. An operator can switch the one
/// outbound request off, and then there is nothing to wait for.
/// </param>
/// <param name="Reading">
/// Nothing where there is no place, where nobody is being asked, or where the
/// provider did not answer. None of the three is a refusal: a tile draws what
/// it has.
/// </param>
public sealed record WeatherResponse(
    string? Place,
    double? Latitude,
    double? Longitude,
    WeatherUnits Units,
    string TemperatureUnit,
    string WindUnit,
    bool Available,
    ReadingResponse? Reading,
    string Attribution,
    DateTimeOffset UpdatedAt)
{
    public static WeatherResponse Of(TheWeather weather) => new(
        weather.Place,
        weather.Latitude,
        weather.Longitude,
        weather.Units,
        weather.Units.Temperature(),
        weather.Units.Wind(),
        weather.Available,
        weather.Reading is { } reading ? ReadingResponse.Of(reading) : null,
        weather.Attribution,
        weather.UpdatedAt);
}

/// <summary>
/// Where the weather is for. Everything empty clears it.
/// </summary>
public sealed record SetWeatherPlaceRequest(
    string? Name,
    double? Latitude,
    double? Longitude,
    WeatherUnits Units = WeatherUnits.Metric);

/// <summary>Somewhere a geocoder thinks the owner might have meant.</summary>
public sealed record PlaceResponse(
    string Name, string? Region, string? Country, double Latitude, double Longitude)
{
    public static PlaceResponse Of(SomewhereCalled place) =>
        new(place.Name, place.Region, place.Country, place.Latitude, place.Longitude);
}

/// <summary>What a place lookup came back with.</summary>
public sealed record PlacesResponse(IReadOnlyList<PlaceResponse> Items);

/// <summary>
/// The weather (<c>docs/api.md</c>, The weather): the one thing in this product
/// that comes from outside it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Its own address, and not part of the dashboard.</strong> The home
/// page is one request over four applications and this is one request to
/// somebody else's server. A home page that could not finish drawing until a
/// provider answered or timed out is exactly what PERSONAL-E9 rules out, and
/// two addresses is how that is guaranteed rather than intended.
/// </para>
/// <para>
/// <strong>Nothing here refuses because of the weather.</strong> No place, no
/// provider, no answer — all of them are a body with no reading in it and the
/// fields beside it saying which. A 503 would be this instance claiming
/// somebody else's outage as its own.
/// </para>
/// <para>
/// <strong>Setting the place and looking one up are the owner's alone.</strong>
/// Both decide what this instance tells an outside service about the person who
/// owns it.
/// </para>
/// </remarks>
public static class WeatherEndpoints
{
    public static IEndpointRouteBuilder MapWeather(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/weather", async (
                HttpResponse response, ReadTheWeather act, CancellationToken cancellationToken) =>
            {
                var weather = await act.ExecuteAsync(cancellationToken);

                // The version of the place, so that saying where the weather is
                // for needs no read in between.
                EntityTags.Write(response, weather.Version);

                return Results.Ok(WeatherResponse.Of(weather));
            })
            .WithName("ReadWeather")
            .WithSummary("What it is doing where the owner said.")
            .WithDescription(
                "The body carries a reading only when there is a place, this instance is asking, and "
                + "the provider answered. `read_at` is when this instance asked, so a client can say "
                + "how old the number is. The attribution is what a free provider is paid in: show it "
                + "beside the number.")
            .Produces<WeatherResponse>();

        endpoints.MapPut("/weather/place", async (
                SetWeatherPlaceRequest body,
                HttpRequest request,
                HttpResponse response,
                SetTheWeatherPlace act,
                CancellationToken cancellationToken) =>
            {
                var weather = await act.ExecuteAsync(
                    body.Name,
                    body.Latitude,
                    body.Longitude,
                    body.Units,
                    EntityTags.Required(request),
                    cancellationToken);

                EntityTags.Write(response, weather.Version);

                return Results.Ok(WeatherResponse.Of(weather));
            })
            .WithName("SetWeatherPlace")
            .WithSummary("Say where the weather is for, or clear it. The owner's alone.")
            .WithDescription(
                "Both coordinates and a name, or all three empty to clear it. The answer carries the "
                + "weather there, so setting a place and seeing it is one request.")
            .Produces<WeatherResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Guarded();

        endpoints.MapGet("/weather/places", async (
                string? q, LookUpAPlace act, CancellationToken cancellationToken) =>
            {
                var places = await act.ExecuteAsync(q, cancellationToken);

                return Results.Ok(new PlacesResponse([.. places.Select(PlaceResponse.Of)]));
            })
            .WithName("LookUpPlaces")
            .WithSummary("The places a geocoder thinks a word names. The owner's alone.")
            .WithDescription(
                "So that saying where the weather is for is typing a name rather than looking two "
                + "numbers up elsewhere. Empty where the provider did not answer or this instance is "
                + "not asking anybody.")
            .Produces<PlacesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
