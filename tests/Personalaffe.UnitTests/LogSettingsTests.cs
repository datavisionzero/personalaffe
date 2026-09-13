using Personalaffe.Application.Ports;

namespace Personalaffe.UnitTests;

public sealed class LogSettingsTests
{
    [Fact]
    public void An_unset_level_is_information()
    {
        Assert.Equal("Information", LogSettings.FromVariables(null).Level);
        Assert.Equal("Information", LogSettings.FromVariables("   ").Level);
    }

    [Theory]
    [InlineData("debug", "Debug")]
    [InlineData("WARNING", "Warning")]
    [InlineData(" Fatal ", "Fatal")]
    public void A_level_is_read_without_regard_to_case_or_surrounding_space(string given, string expected)
    {
        Assert.Equal(expected, LogSettings.FromVariables(given).Level);
    }

    [Fact]
    public void A_level_that_is_not_one_names_the_variable_and_lists_the_levels()
    {
        var refusal = Assert.Throws<ArgumentException>(() => LogSettings.FromVariables("chatty"));

        Assert.Contains(LogSettings.LevelVariable, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Information", refusal.Message, StringComparison.Ordinal);
    }
}
