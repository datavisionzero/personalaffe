using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.Application.Ports;

/// <summary>What is in one folder: the folders first, then the files.</summary>
/// <param name="Chain">
/// Where this folder is, from the root down to it — the breadcrumb, and what
/// tells a caller the folder it asked for still exists. Empty at the root.
/// </param>
public sealed record TheContents(
    IReadOnlyList<Folder> Chain, IReadOnlyList<Folder> Folders, IReadOnlyList<StoredFile> Files);

/// <summary>
/// Where the Files application's metadata is kept: the two tables and the tree
/// they make (<see cref="StoredFile"/>, <see cref="Folder"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The bytes are not here.</strong> They are <see cref="IFileBytes"/>,
/// on a volume, and the split is the application's central fact rather than a
/// tidiness: two stores that can each succeed while the other fails is what the
/// order of writes in <c>docs/adr/0006</c> is about.
/// </para>
/// <para>
/// Nothing here takes a caller. Permission and the application switch are
/// settled by the acts, once, through <c>ReachingAnApplication</c>, so that a
/// store cannot come to its own conclusion about what read access means.
/// </para>
/// </remarks>
public interface IStoredFiles
{
    /// <summary>
    /// What is in <paramref name="folder"/> — the root where that is nothing —
    /// and the chain from the root down to it.
    /// </summary>
    /// <remarks>
    /// It does not check that the folder is there. Whether an address answers
    /// <c>not-found</c> or <c>deleted</c> depends on the instance's retention,
    /// which is the act's to know and not a store's.
    /// </remarks>
    Task<TheContents> ReadAsync(Guid? folder, CancellationToken cancellationToken);

    /// <summary>One file, or nothing at that id. Deleted ones are not found.</summary>
    Task<StoredFile?> FindFileAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One folder, or nothing at that id. Deleted ones are not found.</summary>
    Task<Folder?> FindFolderAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The same, but seeing what is in the Trash — what an address answers
    /// <c>deleted</c> from rather than <c>not-found</c>.
    /// </summary>
    Task<StoredFile?> FindFileEvenDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <inheritdoc cref="FindFileEvenDeletedAsync"/>
    Task<Folder?> FindFolderEvenDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// How many bytes the application is storing, deleted-but-recoverable ones
    /// included.
    /// </summary>
    /// <remarks>
    /// Deleted files count. They are still on the volume and still the owner's
    /// to restore, and a quota that ignored them would be a quota an owner
    /// could walk past by deleting and uploading in turn — and then find they
    /// could not restore what they deleted.
    /// </remarks>
    Task<long> StoredBytesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether something in <paramref name="folder"/> is already called
    /// <paramref name="name"/>, other than <paramref name="itself"/>.
    /// </summary>
    Task<bool> NameIsTakenAsync(
        Guid? folder, string name, Guid? itself, CancellationToken cancellationToken);

    /// <summary>
    /// The folders from <paramref name="folder"/> up to the root, deleted ones
    /// included, and whether the chain reaches the root at all.
    /// </summary>
    /// <remarks>
    /// What a move checks against — a folder cannot be put inside itself — and
    /// what a restore hands to <see cref="Restoration"/>. A chain that does not
    /// reach the root is one whose ancestor was removed for good.
    /// </remarks>
    Task<(IReadOnlyList<Folder> Chain, bool ReachesTheRoot)> ChainAsync(
        Guid folder, CancellationToken cancellationToken);

    /// <summary>
    /// Everything under <paramref name="folder"/>, deleted ones included, the
    /// folder itself first.
    /// </summary>
    /// <remarks>
    /// What a move asks before it lets a folder go somewhere: a folder cannot be
    /// put inside itself, and how deep the thing being moved is decides whether
    /// it still fits under <see cref="Folder.MaxDepth"/> when it lands.
    /// </remarks>
    Task<IReadOnlyList<Folder>> SubtreeAsync(Guid folder, CancellationToken cancellationToken);

    /// <summary>
    /// Every file id this application still has, the Trash included.
    /// </summary>
    /// <remarks>
    /// What the tidy-up compares the volume against. Deleted rows are in it:
    /// their bytes are still the owner's to restore, and removing them because
    /// the file is in the Trash would make restoring a file give back an empty
    /// one.
    /// </remarks>
    Task<IReadOnlySet<Guid>> StoredIdsAsync(CancellationToken cancellationToken);

    /// <summary>Puts a file's row down, its bytes already on the volume.</summary>
    Task AddAsync(StoredFile file, CancellationToken cancellationToken);

    /// <summary>Puts a folder down.</summary>
    Task AddAsync(Folder folder, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the change an act has already made to something tracked, refusing
    /// as <c>stale</c> if the row moved underneath it.
    /// </summary>
    Task SaveAsync(string what, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a file aside, and moves its version on.
    /// </summary>
    Task DeleteAsync(StoredFile file, Caller by, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a folder aside with everything under it, under one moment, so that
    /// the whole thing comes back together (<see cref="Restoration"/>).
    /// </summary>
    Task DeleteAsync(Folder folder, Caller by, DateTimeOffset at, CancellationToken cancellationToken);
}
