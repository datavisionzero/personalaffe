using System.Globalization;

namespace Personalaffe.Domain.Files;

/// <summary>
/// Where one file's bytes are, relative to the storage root: derived from its
/// id and from nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is why path traversal is not a thing that can happen here.</strong>
/// VISION §8 asks that file and path operations never escape the storage area;
/// the way this product keeps that promise is not a filter over what the owner
/// typed but an address the owner never touches. A name is a label on a row
/// (<see cref="FileName"/>); the disk only ever sees thirty-two hexadecimal
/// characters that came out of a <see cref="Guid"/>.
/// </para>
/// <para>
/// <strong>Two levels of fan-out.</strong> <c>files/ab/cd/abcd…</c> rather than
/// <c>files/abcd…</c>, because one directory holding every file an instance has
/// ever stored is a directory that gets slower to list every year, and listing
/// it is what the tidy-up does. Two nibbles each is 256 wide at both levels,
/// which is enough for the small personal storage VISION §6.5 describes and
/// costs nothing when it is nearly empty. The ids are version 7, so the first
/// bytes are time — two files stored the same week land near each other, which
/// is what a backup reading the tree in order wants.
/// </para>
/// <para>
/// The separator is <c>/</c> here and everywhere else this address is written
/// down. It is a relative address in the product's own words, not a path on
/// somebody's operating system; the store that opens it is the one place that
/// turns it into one.
/// </para>
/// </remarks>
public static class StorageAddress
{
    /// <summary>The directory under the storage root that holds stored files.</summary>
    public const string Files = "files";

    /// <summary>
    /// The directory under the storage root that holds uploads still arriving.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Files"/> and not inside it, so that the tidy-up can
    /// tell a file that is being written from a file that is stored without
    /// reading a row: what is in here is by definition not finished, and what
    /// is in <see cref="Files"/> is by definition not being written to.
    /// </remarks>
    public const string Incoming = "incoming";

    /// <summary>Where the bytes of the file with this id are.</summary>
    public static string Of(Guid id)
    {
        var name = id.ToString("N", CultureInfo.InvariantCulture);

        return $"{Files}/{name[..2]}/{name[2..4]}/{name}";
    }

    /// <summary>Where an upload with this id is written while it arrives.</summary>
    public static string Arriving(Guid id) =>
        $"{Incoming}/{id.ToString("N", CultureInfo.InvariantCulture)}.part";

    /// <summary>
    /// The id the address of a stored file belongs to, or nothing where it is
    /// not one of ours.
    /// </summary>
    /// <remarks>
    /// What the tidy-up asks of every file it finds under <see cref="Files"/>:
    /// a name that is not an id at the place an id belongs is something this
    /// product did not put there, and it is left alone rather than removed.
    /// Deleting what we do not recognise is how a sweep destroys an operator's
    /// own file.
    /// </remarks>
    public static Guid? IdAt(string address)
    {
        var parts = (address ?? string.Empty).Split('/');

        if (parts.Length != 4 || parts[0] != Files)
        {
            return null;
        }

        if (!Guid.TryParseExact(parts[3], "N", out var id))
        {
            return null;
        }

        return Of(id) == address ? id : null;
    }
}
