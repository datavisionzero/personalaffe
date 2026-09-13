namespace Personalaffe.UnitTests;

/// <summary>
/// Where the repository begins, found from the test binary rather than from a
/// path somebody's machine happens to have.
/// </summary>
internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(System.IO.Path.Combine(directory.FullName, "Personalaffe.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No Personalaffe.slnx above the test binary.");
    }
}
