namespace Personalaffe.Application.Ports;

/// <summary>What arrived, once the last byte of an upload has landed.</summary>
/// <param name="Address">
/// Where it is, relative to the storage root — <c>StorageAddress.Of</c> of the
/// id it was written under.
/// </param>
public sealed record BytesArrived(Guid Id, string Address, long Size);

/// <summary>
/// Where the bytes of the owner's files are: a volume, and not the database
/// (VISION §9).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here takes a name.</strong> Every address is derived from an
/// id (<c>StorageAddress</c>), which is what makes escaping the storage area
/// impossible rather than filtered — there is no input to this interface that a
/// path could be smuggled through.
/// </para>
/// <para>
/// <strong>An upload is two steps and the order is the design.</strong>
/// <see cref="ReceiveAsync"/> writes the bytes and puts them at their final
/// address; the caller writes the row afterwards. A crash between the two
/// leaves bytes nobody points at, which <see cref="TidyAsync"/> removes on the
/// hour; the other order would leave a row whose file is missing, which is the
/// owner's file gone. <c>docs/adr/0006</c> is where that trade is written down.
/// </para>
/// </remarks>
public interface IFileBytes
{
    /// <summary>
    /// Reads <paramref name="content"/> to its end and stores it under a new id,
    /// refusing as soon as it passes <paramref name="maxBytes"/>.
    /// </summary>
    /// <param name="content">The request's body, read once and not rewound.</param>
    /// <param name="id">
    /// The id the bytes are stored under, which becomes the file's. It is made
    /// by the caller because the address is the id and the bytes go first.
    /// </param>
    /// <param name="maxBytes">
    /// What one file may be. It is enforced as the stream is read rather than
    /// from a declared <c>Content-Length</c>, which a caller writes and nothing
    /// checks.
    /// </param>
    /// <exception cref="Refusal"><c>too-large</c>: it went past the limit.</exception>
    Task<BytesArrived> ReceiveAsync(
        Stream content, Guid id, long maxBytes, CancellationToken cancellationToken);

    /// <summary>The stored bytes, to be read once and handed to the response.</summary>
    /// <exception cref="Refusal">
    /// <c>internal</c>: the row says there is a file and the volume disagrees.
    /// </exception>
    Task<Stream> OpenAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Removes one file's bytes, and says whether there were any.
    /// </summary>
    /// <remarks>
    /// Removing what is not there is not a failure. The purge has to be able to
    /// finish a job it half-finished before the instance was killed, and a
    /// sweep that threw on the first missing file would never reach the second.
    /// </remarks>
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Removes what nothing points at: uploads that never finished, and stored
    /// files whose row is gone. Says how many.
    /// </summary>
    /// <param name="stored">
    /// Every id the metadata store still has, deleted-but-recoverable ones
    /// included. Anything else under <c>files/</c> is an orphan.
    /// </param>
    /// <param name="olderThan">
    /// Only what has not been touched since this moment. An upload in flight is
    /// a file nothing points at yet, and the margin is what keeps the tidy-up
    /// from destroying one.
    /// </param>
    Task<int> TidyAsync(
        IReadOnlySet<Guid> stored, DateTimeOffset olderThan, CancellationToken cancellationToken);
}
