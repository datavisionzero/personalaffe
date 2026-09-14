using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.Application.Acts.Files;

/// <summary>
/// What is in a folder, where it is, and how much room is left.
/// </summary>
/// <remarks>
/// <strong>There is no cursor and no limit</strong>, unlike the Scratchpad's
/// list and the Trash's. This is one folder's contents rather than everything an
/// application holds: a folder with more in it than a screen can draw is a
/// folder the owner made, and the answer to it is another folder, which they
/// have. A page over a tree would also be a page a breadcrumb cannot describe.
/// </remarks>
public sealed class ReadTheFolder(
    ReachingAnApplication reaching,
    IStoredFiles files,
    RetentionSettings retention,
    StorageSettings storage)
{
    public async Task<TheListing> ExecuteAsync(Guid? folder, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Files, cancellationToken);

        if (folder is { } id)
        {
            // Read before the contents, so that a folder that has been deleted
            // since the link was made says `deleted` rather than answering as an
            // empty folder that happens to have nothing in it.
            await TheFiles.FolderAsync(files, retention, id, cancellationToken);
        }

        var contents = await files.ReadAsync(folder, cancellationToken);

        return new TheListing(
            [.. contents.Chain.Select(TheFolder.Of)],
            [.. contents.Folders.Select(TheFolder.Of)],
            [.. contents.Files.Select(TheFile.Of)],
            await files.StoredBytesAsync(cancellationToken),
            storage.MaxFileBytes,
            storage.MaxTotalBytes);
    }
}

/// <summary>One file's metadata, by its own address.</summary>
public sealed class ReadAFile(
    ReachingAnApplication reaching, IStoredFiles files, RetentionSettings retention)
{
    public async Task<TheFile> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Files, cancellationToken);

        return TheFile.Of(await TheFiles.FileAsync(files, retention, id, cancellationToken));
    }
}

/// <summary>
/// One file's bytes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the stable reference</strong> (<c>docs/mvp-plan.md</c>,
/// PERSONAL-E6): the address is the file's id, which is made at the first
/// upload and never changes, so renaming and moving cannot break a link to it.
/// Knowledge links here in PERSONAL-E7 and there is no second attachment store.
/// </para>
/// <para>
/// <strong>It is behind the same door as everything else.</strong> VISION §8 is
/// explicit that file retrievals require authentication and that the MVP has no
/// public content or sharing links; a download that were reachable without a
/// credential would be the one hole in that, and it would be the hole that
/// matters most.
/// </para>
/// </remarks>
public sealed class DownloadAFile(
    ReachingAnApplication reaching, IStoredFiles files, IFileBytes bytes, RetentionSettings retention)
{
    public async Task<TheDownload> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Files, cancellationToken);

        var file = await TheFiles.FileAsync(files, retention, id, cancellationToken);

        return new TheDownload(TheFile.Of(file), await bytes.OpenAsync(file.Id, cancellationToken));
    }
}

/// <summary>
/// Stores a file: the bytes first, then the row.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The order is the design</strong> and is written down in
/// <c>docs/adr/0006</c>. An instance killed between the two leaves bytes nobody
/// points at, which the tidy-up removes on the hour; the other order would leave
/// a row whose file is missing, which is the owner's file gone. One of those
/// failures costs disk and the other costs data.
/// </para>
/// <para>
/// <strong>The limits are enforced against what arrives</strong>, never against
/// a declared <c>Content-Length</c>, which a caller writes and nothing checks.
/// The stream is cut off at whichever limit is nearer — the per-file one, or
/// what is left of the total — so a body that claims nothing cannot fill the
/// volume, and the refusal says which of the two it was.
/// </para>
/// </remarks>
public sealed class UploadAFile(
    ReachingAnApplication reaching,
    IStoredFiles files,
    IFileBytes bytes,
    RetentionSettings retention,
    StorageSettings storage,
    TimeProvider clock)
{
    public async Task<TheFile> ExecuteAsync(
        string? name, Guid? folder, string? mediaType, Stream content, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Files, cancellationToken);

        // The name is held to the rules before a byte is read, so that an upload
        // that was never going to be accepted does not cost the owner their
        // upstream bandwidth first.
        var accepted = FileName.Accepted(name);

        await TheFiles.RoomAtAsync(files, retention, folder, depthBelow: 0, cancellationToken);
        await TheFiles.NameIsFreeAsync(files, folder, accepted, itself: null, cancellationToken);

        var now = clock.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var arrived = await ReceiveAsync(content, id, roomFreedBy: 0, cancellationToken);

        var file = StoredFile.Stored(id, accepted, folder, arrived.Size, mediaType, now);

        try
        {
            await files.AddAsync(file, cancellationToken);
        }
        catch
        {
            // The row did not land, so nothing points at these bytes. The
            // tidy-up would take them within the hour; taking them now is what
            // keeps a client retrying a failing upload from filling the volume
            // with copies of it.
            await bytes.RemoveAsync(id, CancellationToken.None);
            throw;
        }

        return TheFile.Of(file);
    }

    /// <summary>
    /// Reads the body onto the volume, held to whichever limit is nearer.
    /// </summary>
    /// <param name="roomFreedBy">
    /// How many bytes this write is replacing. A file being replaced gives its
    /// own size back first, so that rewriting a file that already fits is never
    /// refused for want of room it is about to release.
    /// </param>
    internal async Task<BytesArrived> ReceiveAsync(
        Stream content, Guid id, long roomFreedBy, CancellationToken cancellationToken)
    {
        var used = await files.StoredBytesAsync(cancellationToken) - roomFreedBy;
        var room = storage.MaxTotalBytes - used;

        if (room <= 0)
        {
            throw Refusal.OutOfSpace(
                $"This instance stores at most {storage.DescribedMaxTotal()} and has no room left. "
                + "Delete something, and empty the Trash if what you deleted is still in it.",
                storage.MaxTotalBytes,
                used);
        }

        var cap = Math.Min(storage.MaxFileBytes, room);

        try
        {
            return await bytes.ReceiveAsync(content, id, cap, cancellationToken);
        }
        catch (Refusal refusal) when (refusal.Code == RefusalCode.TooLarge && cap < storage.MaxFileBytes)
        {
            // It was not the per-file limit that stopped it, it was the room
            // left. Two codes because the caller's move differs: send something
            // smaller, or delete something.
            throw Refusal.OutOfSpace(
                $"This file does not fit in what is left of this instance's {storage.DescribedMaxTotal()}. "
                + "Delete something, and empty the Trash if what you deleted is still in it.",
                storage.MaxTotalBytes,
                used);
        }
    }
}

/// <summary>
/// New bytes for a file that already exists: the id, the name and the place
/// stay, so every link to it now answers with what was just written.
/// </summary>
/// <remarks>
/// <strong>Nothing is kept of what it replaced.</strong> VISION §6.5 rules
/// complex file versioning out of the MVP, and a store that quietly kept every
/// replaced upload would be a quota the owner cannot see and cannot empty. What
/// they have instead is the Trash, for the file as a whole.
/// </remarks>
public sealed class ReplaceTheBytes(
    ReachingAnApplication reaching,
    IStoredFiles files,
    RetentionSettings retention,
    UploadAFile upload,
    TimeProvider clock)
{
    public async Task<TheFile> ExecuteAsync(
        Guid id, string? mediaType, Stream content, ContentVersion held, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Files, cancellationToken);

        var file = await TheFiles.FileAsync(files, retention, id, cancellationToken);

        RequireCurrent(file.Version, held);

        // The new bytes go to the same address, which is the id: the move at the
        // end of `ReceiveAsync` replaces the old file in one step. There is no
        // moment at which the address holds half of either.
        var arrived = await upload.ReceiveAsync(content, id, roomFreedBy: file.Size, cancellationToken);

        file.Replace(arrived.Size, mediaType, clock.GetUtcNow());

        await files.SaveAsync("The file", cancellationToken);

        return TheFile.Of(file);
    }

    private static void RequireCurrent(ContentVersion current, ContentVersion held)
    {
        if (!current.Matches(held))
        {
            throw Refusal.Stale(
                "The file has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.",
                current);
        }
    }
}
