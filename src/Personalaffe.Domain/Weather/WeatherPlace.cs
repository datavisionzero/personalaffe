using System.Globalization;

namespace Personalaffe.Domain.Weather;

/// <summary>Which scale the owner reads a temperature and a wind in.</summary>
/// <remarks>
/// Two values and no more. It is here rather than left at Celsius because the
/// one setting a weather tile has that somebody will certainly want is the one
/// their country uses, and it costs two words in a request to the provider —
/// against a number on a screen that is simply wrong for half the world.
/// </remarks>
public enum WeatherUnits
{
    /// <summary>Degrees Celsius and kilometres per hour.</summary>
    Metric,

    /// <summary>Degrees Fahrenheit and miles per hour.</summary>
    Imperial,
}

/// <summary>
/// Where the owner wants the weather for (VISION §6.1).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One row, and it exists before anybody sets it.</strong> The
/// migration seeds a place that is nowhere, so that reading it is a read rather
/// than a read that might have to insert, and so that it has a version to be
/// written against from the first moment. <see cref="Configured"/> is what
/// tells "the owner has not said yet" from "the owner said here".
/// </para>
/// <para>
/// <strong>It is a point and a label, not an address.</strong> What the
/// provider is asked is two numbers; the name is what the tile writes above
/// them, and this instance never resolves one into the other — looking a place
/// name up is a thing the owner does once, in Settings, through the provider's
/// own geocoder (<c>IWeather.LookUpAsync</c>). Storing the point is what keeps
/// a tile from depending on a second service every time it is drawn.
/// </para>
/// <para>
/// <strong>Coordinates, deliberately, and not a postcode or a city id.</strong>
/// A point is a thing every provider understands, so the one decision this
/// epic makes about a provider is not also a decision about what is stored.
/// </para>
/// </remarks>
public sealed class WeatherPlace
{
    /// <summary>How long a label may be. A line above a temperature.</summary>
    public const int NameMaxLength = 100;

    /// <summary>
    /// How many decimal places a coordinate keeps. Four is about eleven metres,
    /// which is far finer than any forecast and is where rounding stops mattering
    /// — and it keeps the value a client reads identical to the one it sent.
    /// </summary>
    public const int Precision = 4;

    private WeatherPlace()
    {
    }

    /// <summary>
    /// Always true, and the key of the row: one place, held to one by the
    /// schema, the way the owner is (<see cref="Owner.Singleton"/>).
    /// </summary>
    public bool Singleton { get; private init; }

    /// <summary>What the owner calls it, or nothing while there is no place.</summary>
    public string? Name { get; private set; }

    /// <summary>Degrees north, between -90 and 90, or nothing.</summary>
    public double? Latitude { get; private set; }

    /// <summary>Degrees east, between -180 and 180, or nothing.</summary>
    public double? Longitude { get; private set; }

    /// <summary>Which scale to read it in.</summary>
    public WeatherUnits Units { get; private set; }

    /// <summary>When it was last set, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>Whether the owner has said where.</summary>
    public bool Configured => Latitude is not null && Longitude is not null;

    /// <summary>The place a fresh instance has: none, and metric.</summary>
    public static WeatherPlace Nowhere(DateTimeOffset now) =>
        new() { Singleton = true, Units = WeatherUnits.Metric, UpdatedAt = now };

    /// <summary>
    /// Sets it, or clears it where nothing is given, and says whether that
    /// changed anything.
    /// </summary>
    /// <exception cref="Refusal">
    /// <c>validation</c>: a coordinate off the globe, one of the two without
    /// the other, a place with no name, or a name longer than a line.
    /// </exception>
    public bool Set(
        string? name, double? latitude, double? longitude, WeatherUnits units, DateTimeOffset now)
    {
        var (setName, setLatitude, setLongitude) = Accepted(name, latitude, longitude);

        if (setName == Name
            && setLatitude == Latitude
            && setLongitude == Longitude
            && units == Units)
        {
            return false;
        }

        Name = setName;
        Latitude = setLatitude;
        Longitude = setLongitude;
        Units = units;
        UpdatedAt = now;

        return true;
    }

    private static (string?, double?, double?) Accepted(
        string? name, double? latitude, double? longitude)
    {
        var label = (name ?? string.Empty).Trim();

        // Nothing at all is how the owner takes the weather off their home
        // page without hiding the tile: the tile stays and says there is
        // nowhere to look at.
        if (latitude is null && longitude is null && label.Length == 0)
        {
            return (null, null, null);
        }

        if (latitude is null || longitude is null)
        {
            throw Refusal.Validation(
                "latitude",
                "A place is both numbers or neither: send latitude and longitude together, or send "
                + "nothing at all to clear it.");
        }

        if (label.Length == 0)
        {
            throw Refusal.Validation("name", "A place has a name. It is what the tile says above the number.");
        }

        if (label.Length > NameMaxLength)
        {
            throw Refusal.Validation(
                "name",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A name is at most {NameMaxLength} characters, and this one is {label.Length}."));
        }

        return (label, Degrees("latitude", latitude.Value, 90), Degrees("longitude", longitude.Value, 180));
    }

    private static double Degrees(string field, double value, double limit)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > limit)
        {
            throw Refusal.Validation(
                field,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{field} is a number between -{limit} and {limit}."));
        }

        return Math.Round(value, Precision, MidpointRounding.AwayFromZero);
    }
}
