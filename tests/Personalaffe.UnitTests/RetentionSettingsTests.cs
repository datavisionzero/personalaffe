using Personalaffe.Application.Ports;

namespace Personalaffe.UnitTests;

/// <summary>
/// The two periods this instance keeps things for, and what it does with a
/// value it will not accept.
/// </summary>
public sealed class RetentionSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unset_is_thirty_days(string? value)
    {
        Assert.Equal(TimeSpan.FromDays(30), RetentionSettings.FromVariables(value).Trash);
    }

    [Fact]
    public void A_whole_number_is_that_many_days()
    {
        Assert.Equal(TimeSpan.FromDays(7), RetentionSettings.FromVariables(" 7 ").Trash);
        Assert.Equal("7 days", RetentionSettings.FromVariables("7").Described());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3651")]
    [InlineData("-1")]
    [InlineData("30d")]
    [InlineData("thirty")]
    [InlineData("7.5")]
    public void Anything_else_stops_the_start_with_the_name_of_the_variable(string value)
    {
        var refusal = Assert.Throws<ArgumentException>(() => RetentionSettings.FromVariables(value));

        Assert.Contains(RetentionSettings.Variable, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bounds_are_a_day_and_ten_years()
    {
        Assert.Equal(TimeSpan.FromDays(1), RetentionSettings.FromVariables("1").Trash);
        Assert.Equal(TimeSpan.FromDays(3650), RetentionSettings.FromVariables("3650").Trash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_scratchpad_retention_is_seven_days(string? value)
    {
        Assert.Equal(TimeSpan.FromDays(7), RetentionSettings.FromVariables(null, value).Scratchpad);
        Assert.Equal("7 days", RetentionSettings.FromVariables(null, value).DescribedScratchpad());
    }

    [Fact]
    public void The_scratchpad_takes_its_own_whole_number_of_days()
    {
        var settings = RetentionSettings.FromVariables("60", " 3 ");

        // Two periods and not one: thirty days for what was deleted and can be
        // had back, seven for what was never meant to last, and neither number
        // is the other's.
        Assert.Equal(TimeSpan.FromDays(60), settings.Trash);
        Assert.Equal(TimeSpan.FromDays(3), settings.Scratchpad);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3651")]
    [InlineData("-1")]
    [InlineData("7d")]
    [InlineData("a week")]
    [InlineData("7.5")]
    public void A_scratchpad_retention_the_instance_will_not_accept_names_its_own_variable(string value)
    {
        var refusal = Assert.Throws<ArgumentException>(() => RetentionSettings.FromVariables(null, value));

        Assert.Contains(RetentionSettings.ScratchpadVariable, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RetentionSettings.Variable + " ", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_is_thirty_days_and_seven()
    {
        Assert.Equal(TimeSpan.FromDays(30), RetentionSettings.Default.Trash);
        Assert.Equal(TimeSpan.FromDays(7), RetentionSettings.Default.Scratchpad);
    }
}
