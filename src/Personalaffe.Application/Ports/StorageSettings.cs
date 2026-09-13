namespace Personalaffe.Application.Ports;

/// <summary>
/// Where the owner's files live: a directory on a volume, beside the database
/// and not in it.
/// </summary>
/// <remarks>
/// <para>
/// The Files application is PERSONAL-E6's and none of it is here. What is here
/// is the one decision the topology cannot defer: an instance that stores files
/// needs a place to put them that survives a container being recreated, and an
/// operator has to be told at start that the place they named does not work —
/// not on the day they upload something.
/// </para>
/// <para>
/// It is deliberately not under the static web root. A file the owner stored is
/// reached through the API, behind whatever the Files epic decides about
/// permissions; a file sitting under <c>wwwroot</c> is reached by anyone who can
/// guess its name, which is a different product.
/// </para>
/// </remarks>
public sealed record StorageSettings(string Root)
{
    public const string Variable = "PERSONALAFFE_STORAGE_ROOT";

    /// <summary>
    /// Relative to the working directory, which is the repository checkout in
    /// development and <c>/app</c> in the image — where the image overrides it
    /// with the volume's own path anyway.
    /// </summary>
    public const string DefaultRoot = "storage";

    public static StorageSettings FromVariables(string? root)
    {
        var chosen = (root ?? string.Empty).Trim();

        if (chosen.Length == 0)
        {
            return new StorageSettings(DefaultRoot);
        }

        // A path with a NUL or a wildcard in it fails later, in a sentence about
        // the filesystem rather than about the variable somebody set.
        if (chosen.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException($"{Variable} is not a usable path.");
        }

        return new StorageSettings(chosen);
    }

    /// <summary>The root as an absolute path, resolved against <paramref name="workingDirectory"/>.</summary>
    public string ResolvedRoot(string workingDirectory) =>
        Path.GetFullPath(Root, workingDirectory);
}
