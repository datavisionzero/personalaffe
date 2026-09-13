using Personalaffe.Application.Ports;

namespace Personalaffe.UnitTests;

public sealed class DatabaseSettingsTests
{
    private const string Password = "a-password-nobody-should-read";

    [Fact]
    public void An_unset_connection_string_names_the_variable_and_shows_the_shape()
    {
        var refusal = Assert.Throws<ArgumentException>(() => DatabaseSettings.FromConnectionString(null));

        Assert.Contains(DatabaseSettings.Variable, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Host=", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connection_string_with_no_host_is_refused_by_name()
    {
        var refusal = Assert.Throws<ArgumentException>(
            () => DatabaseSettings.FromConnectionString("Database=personalaffe;Username=personalaffe"));

        Assert.Contains(DatabaseSettings.Variable, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("host", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Host=db")]
    [InlineData("Server=db;Database=personalaffe")]
    [InlineData("host=db;port=5432")]
    public void A_connection_string_that_names_a_host_is_taken_as_it_stands(string given)
    {
        Assert.Equal(given, DatabaseSettings.FromConnectionString(given).ConnectionString);
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("PWD")]
    [InlineData("SSL Password")]
    [InlineData("ssl_password")]
    public void Every_spelling_of_a_credential_is_masked(string keyword)
    {
        var settings = DatabaseSettings.FromConnectionString($"Host=db;Database=personalaffe;{keyword}={Password}");

        Assert.DoesNotContain(Password, settings.Redacted, StringComparison.Ordinal);
        Assert.Contains($"{keyword}=***", settings.Redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void What_is_not_a_credential_survives_redaction()
    {
        var settings = DatabaseSettings.FromConnectionString(
            $"Host=db;Port=5432;Database=personalaffe;Username=personalaffe;Password={Password}");

        Assert.Contains("Host=db", settings.Redacted, StringComparison.Ordinal);
        Assert.Contains("Database=personalaffe", settings.Redacted, StringComparison.Ordinal);
        Assert.Contains("Username=personalaffe", settings.Redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, settings.Redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void The_raw_string_is_still_there_for_the_provider_that_needs_it()
    {
        var raw = $"Host=db;Password={Password}";

        Assert.Equal(raw, DatabaseSettings.FromConnectionString(raw).ConnectionString);
    }
}
