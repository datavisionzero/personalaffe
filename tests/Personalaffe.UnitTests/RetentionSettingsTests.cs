using Personalaffe.Application.Ports;

namespace Personalaffe.UnitTests;

/// <summary>
/// How long the Trash keeps things, and what an instance does with a value it
/// will not accept.
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
}
