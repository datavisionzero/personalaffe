using Personalaffe.Application.Ports;

namespace Personalaffe.UnitTests;

/// <summary>
/// The optional address an operator names, and what the instance refuses to
/// start with.
/// </summary>
public sealed class PublicUrlSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unset_is_a_working_instance_and_not_a_missing_setting(string? value) =>
        Assert.Null(PublicUrlSettings.FromVariables(value).Url);

    [Fact]
    public void An_address_is_kept_whole()
    {
        var settings = PublicUrlSettings.FromVariables(" https://workspace.example.com ");

        Assert.Equal("workspace.example.com", settings.Url!.Host);
        Assert.Equal("https", settings.Url.Scheme);
    }

    [Theory]
    [InlineData("workspace.example.com")]
    [InlineData("/workspace")]
    [InlineData("ftp://workspace.example.com")]
    public void Something_that_is_not_an_address_stops_the_start(string value)
    {
        var refusal = Assert.Throws<ArgumentException>(() => PublicUrlSettings.FromVariables(value));

        Assert.Contains(PublicUrlSettings.Variable, refusal.Message, StringComparison.Ordinal);
    }
}
