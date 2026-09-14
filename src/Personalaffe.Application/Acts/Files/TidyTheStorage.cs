using Personalaffe.Application.Ports;

namespace Personalaffe.Application.Acts.Files;

/// <summary>
/// Removes from the volume what nothing points at: uploads that never finished,
/// and files whose row is gone.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the other half of "bytes first, then the row".</strong> That
/// order is what keeps an instance killed mid-upload from leaving a row whose
/// file is missing; what it leaves instead is bytes nobody points at, and this
/// is what takes them away. Without it the trade would be "lose disk slowly"
/// rather than "lose nothing", and an operator would eventually find a volume
/// full of failed uploads.
/// </para>
/// <para>
/// <strong>It has no caller and no enablement</strong>, exactly as
/// <see cref="PurgeTheTrash"/> and <c>ExpireTheEntries</c> do not: this is the
/// instance tidying up after itself, and an act that asked who was calling would
/// be an act that could be called. Switching Files off does not suspend it —
/// what it removes was never the owner's content in the first place.
/// </para>
/// <para>
/// <strong>The margin is what keeps it safe.</strong> An upload in flight is a
/// file nothing points at yet, and the only thing separating it from an orphan
/// is how recently it was written. An hour is far longer than any upload this
/// instance will accept — 64 MiB by default — and short enough that a failed
/// one does not sit there for a day.
/// </para>
/// <para>
/// <strong>What it does not recognise, it leaves alone.</strong> A file under
/// the storage root whose name is not one this product writes is an operator's
/// own — a restore somebody unpacked by hand, a note beside the volume — and a
/// sweep that removed what it did not recognise would eventually remove
/// something that mattered.
/// </para>
/// </remarks>
public sealed class TidyTheStorage(IStoredFiles files, IFileBytes bytes)
{
    /// <summary>How recently a file must have been written to be left alone.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromHours(1);

    public async Task<int> ExecuteAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        // The rows are read first, so that anything stored between this read and
        // the walk of the volume looks like an orphan and is saved by the margin
        // rather than by luck.
        var stored = await files.StoredIdsAsync(cancellationToken);

        return await bytes.TidyAsync(stored, olderThan, cancellationToken);
    }
}
