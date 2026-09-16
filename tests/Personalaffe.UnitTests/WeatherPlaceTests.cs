using Personalaffe.Domain;
using Personalaffe.Domain.Weather;

namespace Personalaffe.UnitTests;

/// <summary>
/// Where the owner wants the weather for: a point, a label and a scale, held to
/// the globe.
/// </summary>
public sealed class WeatherPlaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_instance_has_a_place_and_it_is_nowhere()
    {
        // It exists before anybody sets it, so that reading it is a read and it
        // has a version to be written against from the first moment.
        var place = WeatherPlace.Nowhere(Now);

        Assert.False(place.Configured);
        Assert.Null(place.Name);
        Assert.Equal(WeatherUnits.Metric, place.Units);
    }

    [Fact]
    public void Setting_a_place_keeps_the_point_and_the_label()
    {
        var place = WeatherPlace.Nowhere(Now);

        Assert.True(place.Set("Wuppertal", 51.2562, 7.1508, WeatherUnits.Metric, Now.AddMinutes(1)));

        Assert.True(place.Configured);
        Assert.Equal("Wuppertal", place.Name);
        Assert.Equal(51.2562, place.Latitude);
        Assert.Equal(7.1508, place.Longitude);
    }

    [Fact]
    public void A_coordinate_is_kept_to_the_precision_a_forecast_has()
    {
        var place = WeatherPlace.Nowhere(Now);
        place.Set("Somewhere", 51.256213456, 7.150887654, WeatherUnits.Metric, Now);

        // What a client reads is what it can send back and have recognised.
        Assert.Equal(51.2562, place.Latitude);
        Assert.Equal(7.1509, place.Longitude);
    }

    [Fact]
    public void Nothing_at_all_clears_it()
    {
        var place = WeatherPlace.Nowhere(Now);
        place.Set("Wuppertal", 51.2562, 7.1508, WeatherUnits.Metric, Now);

        Assert.True(place.Set(null, null, null, WeatherUnits.Metric, Now.AddMinutes(1)));

        Assert.False(place.Configured);
        Assert.Null(place.Name);
    }

    [Fact]
    public void Setting_it_to_what_it_already_is_is_not_a_write()
    {
        var place = WeatherPlace.Nowhere(Now);
        place.Set("Wuppertal", 51.2562, 7.1508, WeatherUnits.Metric, Now);

        var version = place.Version;

        Assert.False(place.Set("Wuppertal", 51.2562, 7.1508, WeatherUnits.Metric, Now.AddHours(1)));
        Assert.True(place.Version.Matches(version));
    }

    [Fact]
    public void Changing_only_the_scale_is_a_write()
    {
        var place = WeatherPlace.Nowhere(Now);
        place.Set("Wuppertal", 51.2562, 7.1508, WeatherUnits.Metric, Now);

        Assert.True(place.Set("Wuppertal", 51.2562, 7.1508, WeatherUnits.Imperial, Now.AddHours(1)));
        Assert.Equal(WeatherUnits.Imperial, place.Units);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void A_point_that_is_not_on_the_globe_is_refused(double latitude, double longitude)
    {
        var place = WeatherPlace.Nowhere(Now);

        var refusal = Assert.Throws<Refusal>(
            () => place.Set("Nowhere", latitude, longitude, WeatherUnits.Metric, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Fact]
    public void One_coordinate_without_the_other_is_refused()
    {
        var place = WeatherPlace.Nowhere(Now);

        Assert.Equal(
            RefusalCode.Validation,
            Assert.Throws<Refusal>(
                () => place.Set("Half a place", 51.2562, null, WeatherUnits.Metric, Now)).Code);
    }

    [Fact]
    public void A_point_with_no_name_is_refused_because_the_tile_writes_one_above_it()
    {
        var place = WeatherPlace.Nowhere(Now);

        Assert.Equal(
            RefusalCode.Validation,
            Assert.Throws<Refusal>(
                () => place.Set("  ", 51.2562, 7.1508, WeatherUnits.Metric, Now)).Code);
    }

    [Fact]
    public void A_name_longer_than_a_line_is_refused()
    {
        var place = WeatherPlace.Nowhere(Now);

        Assert.Equal(
            RefusalCode.Validation,
            Assert.Throws<Refusal>(() => place.Set(
                new string('a', WeatherPlace.NameMaxLength + 1),
                51.2562,
                7.1508,
                WeatherUnits.Metric,
                Now)).Code);
    }
}

/// <summary>What the sky is doing, in words both clients say the same way.</summary>
public sealed class SkyTests
{
    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(3, "Overcast")]
    [InlineData(61, "Light rain")]
    [InlineData(95, "Thunderstorm")]
    public void The_codes_a_provider_answers_with_have_words(int code, string words)
    {
        Assert.Equal(words, Sky.Described(code));
    }

    [Fact]
    public void A_code_nobody_here_has_a_word_for_shows_up_as_itself()
    {
        // Something odd on a tile, rather than a blank: a provider that adds a
        // code should be noticed.
        Assert.Equal("Weather code 42", Sky.Described(42));
    }
}
