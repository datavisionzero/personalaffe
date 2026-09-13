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
}
