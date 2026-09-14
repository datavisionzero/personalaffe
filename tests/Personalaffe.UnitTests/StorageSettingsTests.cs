using Personalaffe.Application.Ports;

namespace Personalaffe.UnitTests;

public sealed class StorageSettingsTests
{
    [Fact]
    public void An_unset_root_is_a_directory_beside_the_instance()
    {
        Assert.Equal(StorageSettings.DefaultRoot, StorageSettings.FromVariables(null).Root);
        Assert.Equal(StorageSettings.DefaultRoot, StorageSettings.FromVariables("   ").Root);
    }

    [Fact]
    public void A_root_is_taken_as_it_stands_and_resolved_against_the_working_directory()
    {
        var relative = StorageSettings.FromVariables("files");

        Assert.Equal(
            Path.Combine(Path.GetFullPath("/somewhere"), "files"),
            relative.ResolvedRoot("/somewhere"));
    }

    [Fact]
    public void An_absolute_root_is_where_it_says_whatever_the_working_directory_is()
    {
        var absolute = StorageSettings.FromVariables(Path.GetFullPath("/var/lib/personalaffe/files"));

        Assert.Equal(
            Path.GetFullPath("/var/lib/personalaffe/files"),
            absolute.ResolvedRoot("/somewhere/else"));
    }

    [Fact]
    public void A_path_no_filesystem_would_take_is_refused_by_the_variable_s_name()
    {
        var refusal = Assert.Throws<ArgumentException>(() => StorageSettings.FromVariables("files\0hidden"));

        Assert.Contains(StorageSettings.Variable, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_limits_have_defaults_and_the_defaults_are_sizes_a_person_recognises()
    {
        var settings = StorageSettings.FromVariables(null);

        Assert.Equal(StorageSettings.DefaultMaxFileMib * StorageSettings.Mebibyte, settings.MaxFileBytes);
        Assert.Equal(StorageSettings.DefaultMaxTotalMib * StorageSettings.Mebibyte, settings.MaxTotalBytes);
        Assert.Equal("64 MiB", settings.DescribedMaxFile());
        Assert.Equal("5120 MiB", settings.DescribedMaxTotal());
    }

    [Fact]
    public void A_limit_is_a_whole_number_of_mebibytes()
    {
        var settings = StorageSettings.FromVariables(null, "8", "128");

        Assert.Equal(8 * StorageSettings.Mebibyte, settings.MaxFileBytes);
        Assert.Equal(128 * StorageSettings.Mebibyte, settings.MaxTotalBytes);
    }

    [Theory]
    [InlineData("64 MiB")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1048577")]
    public void A_limit_that_is_not_one_stops_the_start_and_names_its_variable(string asked)
    {
        var refusal = Assert.Throws<ArgumentException>(() => StorageSettings.FromVariables(null, asked));

        Assert.Contains(StorageSettings.MaxFileVariable, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_per_file_limit_over_the_total_would_mean_nothing_could_be_stored()
    {
        var refusal = Assert.Throws<ArgumentException>(() => StorageSettings.FromVariables(null, "512", "64"));

        Assert.Contains(StorageSettings.MaxFileVariable, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(StorageSettings.MaxTotalVariable, refusal.Message, StringComparison.Ordinal);
    }
}
