using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.Application.Acts.Files;

/// <summary>One file, as a caller sees it.</summary>
public sealed record TheFile(
    Guid Id,
    string Name,
    Guid? Folder,
    long Size,
    string MediaType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static TheFile Of(StoredFile file) => new(
        file.Id, file.Name, file.FolderId, file.Size, file.MediaType, file.CreatedAt, file.UpdatedAt);
}

/// <summary>One folder, as a caller sees it.</summary>
public sealed record TheFolder(
    Guid Id, string Name, Guid? Parent, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static TheFolder Of(Folder folder) => new(
        folder.Id, folder.Name, folder.ParentId, folder.CreatedAt, folder.UpdatedAt);
}

/// <summary>
/// What is in one folder, where it is, and how much room is left.
/// </summary>
/// <param name="Chain">
/// The folders from the root down to the one being read — the breadcrumb.
/// Empty at the root.
/// </param>
/// <param name="UsedBytes">
/// How much the application is storing, what is in the Trash included: those
/// bytes are still on the volume and still the owner's to restore.
/// </param>
/// <remarks>
/// The limits travel with every listing because the screen that draws it is the
/// screen somebody is about to upload from, and "how much room is left" is a
/// question a client should not have to ask separately or work out from a
/// refusal.
/// </remarks>
public sealed record TheListing(
    IReadOnlyList<TheFolder> Chain,
    IReadOnlyList<TheFolder> Folders,
    IReadOnlyList<TheFile> Files,
    long UsedBytes,
    long MaxFileBytes,
    long MaxTotalBytes);

/// <summary>A file's bytes, and what the response should say they are.</summary>
/// <remarks>
/// The stream is the volume's and is read once, straight into the response.
/// Nothing between here and the socket holds the whole file: the instance can
/// store more of one than it should ever put in memory at once.
/// </remarks>
public sealed record TheDownload(TheFile File, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// What the Files acts have in common: the refusals they share, and the three
/// questions every write in a tree has to ask.
/// </summary>
internal static class TheFiles
{
    internal static Refusal NoSuchFile(Guid id) =>
        Refusal.NotFound($"Nothing with the id {id} is a file in this workspace.");

    internal static Refusal NoSuchFolder(Guid id) =>
        Refusal.NotFound($"Nothing with the id {id} is a folder in this workspace.");

    /// <summary>
    /// The file at <paramref name="id"/>, or the refusal its address answers:
    /// <c>deleted</c> while it is in the Trash, <c>not-found</c> otherwise.
    /// </summary>
    internal static async Task<StoredFile> FileAsync(
        IStoredFiles files, RetentionSettings retention, Guid id, CancellationToken cancellationToken)
    {
        var file = await files.FindFileEvenDeletedAsync(id, cancellationToken) ?? throw NoSuchFile(id);

        return file.IsDeleted()
            ? throw file.Gone($"The file `{file.Name}`", retention.Trash)
            : file;
    }

    /// <inheritdoc cref="FileAsync"/>
    internal static async Task<Folder> FolderAsync(
        IStoredFiles files, RetentionSettings retention, Guid id, CancellationToken cancellationToken)
    {
        var folder = await files.FindFolderEvenDeletedAsync(id, cancellationToken) ?? throw NoSuchFolder(id);

        return folder.IsDeleted()
            ? throw folder.Gone($"The folder `{folder.Name}`", retention.Trash)
            : folder;
    }

    /// <summary>
    /// The place something is going: it has to exist, be out of the Trash, and
    /// have room under it for one more level.
    /// </summary>
    /// <param name="depthBelow">
    /// How many levels the thing being put there brings with it: 0 for a file,
    /// and the height of its subtree for a folder.
    /// </param>
    /// <exception cref="Refusal">
    /// <c>not-found</c> or <c>deleted</c>: there is no such folder to put
    /// anything in. <c>conflict</c>: the tree would be deeper than it goes.
    /// </exception>
    internal static async Task RoomAtAsync(
        IStoredFiles files,
        RetentionSettings retention,
        Guid? destination,
        int depthBelow,
        CancellationToken cancellationToken)
    {
        if (destination is not { } id)
        {
            return;
        }

        await FolderAsync(files, retention, id, cancellationToken);

        var (chain, _) = await files.ChainAsync(id, cancellationToken);

        if (chain.Count + depthBelow + 1 > Folder.MaxDepth)
        {
            throw Refusal.Conflict(
                $"This workspace's folders go {Folder.MaxDepth} deep, and that would be deeper. "
                + "Put it somewhere nearer the top.");
        }
    }

    /// <summary>
    /// Whether anything is already called this where it is going.
    /// </summary>
    /// <exception cref="Refusal">
    /// <c>conflict</c>: something is. It is never a silent rename — two things
    /// with one name in one folder is a tree nobody can navigate, and a product
    /// that quietly appends "(2)" has made a decision the owner would have made
    /// differently.
    /// </exception>
    internal static async Task NameIsFreeAsync(
        IStoredFiles files, Guid? folder, string name, Guid? itself, CancellationToken cancellationToken)
    {
        if (await files.NameIsTakenAsync(folder, name, itself, cancellationToken))
        {
            throw Refusal.Conflict(
                $"Something in that folder is already called `{name}`. Names in one folder are one each, "
                + "whatever their capitals.");
        }
    }
}
