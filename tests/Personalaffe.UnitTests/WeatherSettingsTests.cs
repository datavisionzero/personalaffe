using Personalaffe.Application.Ports;

namespace Personalaffe.UnitTests;

/// <summary>
/// The operator's two decisions about the one outbound request this instance
/// makes: whether it is made, and how often.
/// </summary>
public sealed class WeatherSettingsTests
{
    [Fact]
    public void An_instance_nobody_configured_asks_every_quarter_of_an_hour()
    {
        var settings = WeatherSettings.FromVariables(null);

        Assert.True(settings.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(WeatherSettings.DefaultFreshnessMinutes), settings.Freshness);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData(" OFF ")]
    public void Off_is_a_real_setting_however_an_operator_spells_it(string spelled)
    {
        Assert.False(WeatherSettings.FromVariables(spelled).Enabled);
        Assert.Equal("switched off", WeatherSettings.FromVariables(spelled).Described());
    }

    [Theory]
    [InlineData("on")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("yes")]
    public void On_is_the_other_half_of_the_same_vocabulary(string spelled)
    {
        Assert.True(WeatherSettings.FromVariables(spelled).Enabled);
    }

    [Fact]
    public void A_word_that_is_neither_stops_the_start_with_the_name_of_the_variable()
    {
        var refusal = Assert.Throws<ArgumentException>(() => WeatherSettings.FromVariables("sometimes"));

        Assert.Contains(WeatherSettings.Variable, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Freshness_is_a_whole_number_of_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), WeatherSettings.FromVariables(null, "30").Freshness);
        Assert.Equal("every 30 minutes", WeatherSettings.FromVariables(null, "30").Described());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1441")]
    [InlineData("half an hour")]
    [InlineData("-5")]
    public void A_freshness_this_instance_will_not_accept_names_its_variable(string spelled)
    {
        var refusal = Assert.Throws<ArgumentException>(
            () => WeatherSettings.FromVariables(null, spelled));

        Assert.Contains(WeatherSettings.FreshnessVariable, refusal.Message, StringComparison.Ordinal);
    }
}
