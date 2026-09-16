using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Weather;

namespace Personalaffe.Infrastructure.Weather;

/// <summary>
/// The weather, from Open-Meteo (<see cref="IWeather"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this provider.</strong> VISION.md asks for a weather tile
/// provided it does not require a disproportionate external service, and this
/// is the one that costs nothing to satisfy: no account, no API key, no
/// contract, no secret in an operator's environment, and a free tier meant for
/// exactly this — one person's instance asking about one point four times an
/// hour. What is owed in return is attribution, which the tile carries
/// (<c>docs/adr/0009</c>).
/// </para>
/// <para>
/// <strong>One socket, held open, and no package to open it with.</strong> This
/// is the only outbound request in the product, so it owns one
/// <see cref="HttpClient"/> over a handler with a bounded connection lifetime
/// rather than bringing an HTTP factory into a layer that otherwise talks to
/// Postgres and a disk. The timeout is short on purpose: a tile that is late is
/// a tile that says it does not know.
/// </para>
/// <para>
/// <strong>Nothing gets out of here as an exception.</strong> A refused
/// connection, a 500, a body that is not what was expected, a timeout — all of
/// them are "no reading", logged once at information and never at warning,
/// because somebody else's server having a bad afternoon is not a fault in this
/// instance and must not read like one in an operator's log.
/// </para>
/// <para>
/// <strong>What it sends is a point.</strong> Not the owner's address, not a
/// session, not a header naming this instance beyond the default. The provider
/// is told two coordinates rounded to four places and nothing else about who is
/// asking.
/// </para>
/// </remarks>
public sealed class OpenMeteo : IWeather, IDisposable
{
    /// <summary>Where a reading comes from.</summary>
    public const string Forecast = "https://api.open-meteo.com/v1/forecast";

    /// <summary>Where a place name is turned into a point, once, in Settings.</summary>
    public const string Geocoder = "https://geocoding-api.open-meteo.com/v1/search";

    /// <summary>Who is owed the credit, as the tile and the CLI print it.</summary>
    public const string Attribution = "Weather data by Open-Meteo.com";

    /// <summary>
    /// How long this waits. Short, because what is behind it is one tile on a
    /// page that has already drawn everything else.
    /// </summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>How many candidates a place lookup offers.</summary>
    public const int Candidates = 8;

    private readonly HttpClient _http;
    private readonly WeatherSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<OpenMeteo> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private (string Where, Reading Reading)? _held;

    public OpenMeteo(WeatherSettings settings, TimeProvider clock, ILogger<OpenMeteo> log)
    {
        _settings = settings;
        _clock = clock;
        _log = log;
        _http = new HttpClient(
            new SocketsHttpHandler
            {
                // Long-lived by design, so the name is looked up again now and
                // then rather than once at start and never afterwards.
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            })
        {
            Timeout = Patience,
        };
    }

    public bool Available => _settings.Enabled;

    string IWeather.Attribution => Attribution;

    public async Task<Reading?> ReadAsync(WeatherPlace place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (!_settings.Enabled || !place.Configured)
        {
            return null;
        }

        var where = Where(place);

        // Held while it is fresh, so that a home page refreshing every fifteen
        // seconds troubles somebody else's servers four times an hour and not
        // two hundred and forty.
        if (Held(where) is { } fresh)
        {
            return fresh;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Asked again inside the gate: several tabs refreshing at once are
            // one request, and the ones that waited get its answer.
            if (Held(where) is { } arrived)
            {
                return arrived;
            }

            var answer = await AskAsync<Answered>(
                $"{Forecast}?latitude={Degrees(place.Latitude)}&longitude={Degrees(place.Longitude)}"
                + "&current=temperature_2m,apparent_temperature,is_day,weather_code,wind_speed_10m"
                + "&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto"
                + $"&temperature_unit={Temperature(place.Units)}&wind_speed_unit={Wind(place.Units)}",
                cancellationToken);

            if (answer?.Current is not { } current)
            {
                return null;
            }

            var reading = new Reading(
                current.Temperature,
                current.FeelsLike,
                answer.Daily?.High?.FirstOrDefault(),
                answer.Daily?.Low?.FirstOrDefault(),
                current.Wind,
                current.Code,
                current.IsDay != 0,
                _clock.GetUtcNow());

            _held = (where, reading);

            return reading;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SomewhereCalled>> LookUpAsync(
        string query, CancellationToken cancellationToken)
    {
        var asked = (query ?? string.Empty).Trim();

        if (!_settings.Enabled || asked.Length == 0)
        {
            return [];
        }

        var answer = await AskAsync<Places>(
            $"{Geocoder}?name={Uri.EscapeDataString(asked)}&count={Candidates}&language=en&format=json",
            cancellationToken);

        return
        [
            .. (answer?.Results ?? []).Select(place => new SomewhereCalled(
                place.Name, place.Region, place.Country, place.Latitude, place.Longitude)),
        ];
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private Reading? Held(string where) =>
        _held is { } held
        && held.Where == where
        && _clock.GetUtcNow() - held.Reading.ReadAt < _settings.Freshness
            ? held.Reading
            : null;

    private async Task<TAnswer?> AskAsync<TAnswer>(string address, CancellationToken cancellationToken)
        where TAnswer : class
    {
        try
        {
            using var response = await _http.GetAsync(address, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogInformation(
                    "The weather provider answered {Status}. The tile will say it does not know.",
                    (int)response.StatusCode);

                return null;
            }

            return await response.Content.ReadFromJsonAsync<TAnswer>(cancellationToken);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException
            or System.Text.Json.JsonException)
        {
            // Not a warning. Somebody else's server is not this instance's
            // health, and an operator reading a log during a real outage should
            // not have to rule this out first.
            if (!cancellationToken.IsCancellationRequested)
            {
                _log.LogInformation(
                    failure, "The weather provider did not answer. The tile will say it does not know.");
            }

            return null;
        }
    }

    /// <summary>What a held reading is held for: the point and the scale.</summary>
    private static string Where(WeatherPlace place) =>
        $"{Degrees(place.Latitude)},{Degrees(place.Longitude)},{place.Units}";

    private static string Degrees(double? value) =>
        (value ?? 0).ToString(
            "0.####", CultureInfo.InvariantCulture);

    private static string Temperature(WeatherUnits units) =>
        units == WeatherUnits.Imperial ? "fahrenheit" : "celsius";

    private static string Wind(WeatherUnits units) => units == WeatherUnits.Imperial ? "mph" : "kmh";

    /// <summary>What the forecast answers, in the shape it answers it.</summary>
    private sealed record Answered(
        [property: JsonPropertyName("current")] Now? Current,
        [property: JsonPropertyName("daily")] Today? Daily);

    private sealed record Now(
        [property: JsonPropertyName("temperature_2m")] double Temperature,
        [property: JsonPropertyName("apparent_temperature")] double? FeelsLike,
        [property: JsonPropertyName("wind_speed_10m")] double Wind,
        [property: JsonPropertyName("weather_code")] int Code,
        [property: JsonPropertyName("is_day")] int IsDay);

    private sealed record Today(
        [property: JsonPropertyName("temperature_2m_max")] IReadOnlyList<double>? High,
        [property: JsonPropertyName("temperature_2m_min")] IReadOnlyList<double>? Low);

    /// <summary>What the geocoder answers.</summary>
    private sealed record Places(
        [property: JsonPropertyName("results")] IReadOnlyList<Somewhere>? Results);

    private sealed record Somewhere(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("admin1")] string? Region,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude);
}
