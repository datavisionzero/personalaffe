using System.Globalization;

namespace Personalaffe.Application.Ports;

/// <summary>
/// Where the owner's files live — a directory on a volume, beside the database
/// and not in it — and how much of it they may use.
/// </summary>
/// <remarks>
/// <para>
/// The root is the one decision the topology cannot defer: an instance that
/// stores files needs a place to put them that survives a container being
/// recreated, and an operator has to be told at start that the place they named
/// does not work — not on the day they upload something. That check is
/// <c>StorageService</c> and it predates the Files application by an epic.
/// </para>
/// <para>
/// It is deliberately not under the static web root. A file the owner stored is
/// reached through the API, behind the same door as everything else; a file
/// sitting under <c>wwwroot</c> is reached by anyone who can guess its name,
/// which is a different product.
/// </para>
/// <para>
/// <strong>The two limits are VISION §8's protection against resource
/// exhaustion</strong>, and VISION §14.4 leaves their numbers to PERSONAL-E6.
/// They are the operator's and not a screen: the owner's answer to a full
/// instance is to delete something, which is a verb they already have, and the
/// person who can make the volume bigger is the person with the variable.
/// </para>
/// <para>
/// Mebibytes and not bytes. The unit is the one an operator already thinks in
/// when they size a volume, a whole number of them is a value that fits in a
/// Compose file without counting zeroes, and it is the same shape as
/// <see cref="RetentionSettings"/>'s whole number of days — nothing in this
/// product asks an operator to spell a duration or a size in a syntax of its
/// own.
/// </para>
/// </remarks>
public sealed record StorageSettings(string Root, long MaxFileBytes, long MaxTotalBytes)
{
    public const string Variable = "PERSONALAFFE_STORAGE_ROOT";

    public const string MaxFileVariable = "PERSONALAFFE_MAX_FILE_MIB";

    public const string MaxTotalVariable = "PERSONALAFFE_MAX_STORAGE_MIB";

    /// <summary>
    /// Relative to the working directory, which is the repository checkout in
    /// development and <c>/app</c> in the image — where the image overrides it
    /// with the volume's own path anyway.
    /// </summary>
    public const string DefaultRoot = "storage";

    /// <summary>
    /// How large one file may be.
    /// </summary>
    /// <remarks>
    /// 64 MiB is the "smaller files" of VISION §6.5 with room to spare: a
    /// scanned document, a photograph, a signed PDF, an archive of a project's
    /// notes. It is deliberately not the size of a video — VISION §11 rules
    /// out media streaming and gives no guarantee for very large files — and an
    /// owner who needs more raises the variable for their own instance.
    /// </remarks>
    public const int DefaultMaxFileMib = 64;

    /// <summary>
    /// How much the whole application may store.
    /// </summary>
    /// <remarks>
    /// 5 GiB is a personal file area rather than a backup target, and it is
    /// small enough that the first refusal arrives long before the volume does.
    /// An instance that runs out of disk with no limit in front of it fails at
    /// whatever happens to write next, which may be Postgres.
    /// </remarks>
    public const int DefaultMaxTotalMib = 5 * 1024;

    public const int MinimumMib = 1;

    /// <summary>One mebibyte, as the arithmetic below counts it.</summary>
    public const long Mebibyte = 1024 * 1024;

    /// <summary>
    /// The largest either limit may be set to: a tebibyte, which is past the
    /// point where this product is the right one and well short of anything
    /// that overflows.
    /// </summary>
    public const int MaximumMib = 1024 * 1024;

    public static StorageSettings FromVariables(
        string? root, string? maxFileMib = null, string? maxTotalMib = null)
    {
        var chosen = (root ?? string.Empty).Trim();

        if (chosen.Length == 0)
        {
            chosen = DefaultRoot;
        }
        else if (chosen.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            // A path with a NUL or a wildcard in it fails later, in a sentence
            // about the filesystem rather than about the variable somebody set.
            throw new ArgumentException($"{Variable} is not a usable path.");
        }

        var file = Size(MaxFileVariable, maxFileMib, DefaultMaxFileMib);
        var total = Size(MaxTotalVariable, maxTotalMib, DefaultMaxTotalMib);

        if (file > total)
        {
            throw new ArgumentException(
                $"{MaxFileVariable} is larger than {MaxTotalVariable}, so no file could ever be stored.");
        }

        return new StorageSettings(chosen, file, total);
    }

    /// <summary>The root as an absolute path, resolved against <paramref name="workingDirectory"/>.</summary>
    public string ResolvedRoot(string workingDirectory) =>
        Path.GetFullPath(Root, workingDirectory);

    /// <summary>How the per-file limit reads in a log line and in a refusal.</summary>
    public string DescribedMaxFile() => Described(MaxFileBytes);

    /// <summary>How the total reads in the same places.</summary>
    public string DescribedMaxTotal() => Described(MaxTotalBytes);

    private static string Described(long bytes) => string.Create(
        CultureInfo.InvariantCulture, $"{bytes / Mebibyte} MiB");

    private static long Size(string variable, string? mib, int fallback)
    {
        var chosen = (mib ?? string.Empty).Trim();

        if (chosen.Length == 0)
        {
            return fallback * Mebibyte;
        }

        if (!int.TryParse(chosen, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            throw new ArgumentException($"{variable} is a whole number of mebibytes.");
        }

        if (whole is < MinimumMib or > MaximumMib)
        {
            throw new ArgumentException($"{variable} is between {MinimumMib} and {MaximumMib} MiB.");
        }

        return whole * Mebibyte;
    }
}
