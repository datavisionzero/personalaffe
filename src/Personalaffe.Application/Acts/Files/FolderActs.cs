using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.Application.Acts.Files;

/// <summary>Makes a folder.</summary>
public sealed class MakeAFolder(
    ReachingAnApplication reaching,
    IStoredFiles files,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task<TheFolder> ExecuteAsync(
        string? name, Guid? parent, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Files, cancellationToken);

        var accepted = FileName.Accepted(name);

        await TheFiles.RoomAtAsync(files, retention, parent, depthBelow: 0, cancellationToken);
        await TheFiles.NameIsFreeAsync(files, parent, accepted, itself: null, cancellationToken);

        var folder = Folder.Make(accepted, parent, clock.GetUtcNow());

        await files.AddAsync(folder, cancellationToken);

        return TheFolder.Of(folder);
    }
}

/// <summary>
/// Renames a file, moves it, or both.
/// </summary>
/// <remarks>
/// <strong>One write and not two</strong>, for the reason the Scratchpad's one
/// write gives: a rename and a move are both changes to the same row, and a
/// second address would be a second place the guard has to be got right. It is
/// also what makes "move it there and call it something else" one act, which is
/// what the owner does when the name they want is taken.
/// </remarks>
public sealed class MoveOrRenameAFile(
    ReachingAnApplication reaching,
    IStoredFiles files,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task<TheFile> ExecuteAsync(
        Guid id, string? name, Guid? folder, ContentVersion held, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Files, cancellationToken);

        var file = await TheFiles.FileAsync(files, retention, id, cancellationToken);

        Current(file.Version, held, "The file");

        var accepted = FileName.Accepted(name);

        await TheFiles.RoomAtAsync(files, retention, folder, depthBelow: 0, cancellationToken);
        await TheFiles.NameIsFreeAsync(files, folder, accepted, id, cancellationToken);

        if (file.Change(accepted, folder, clock.GetUtcNow()))
        {
            await files.SaveAsync("The file", cancellationToken);
        }

        return TheFile.Of(file);
    }

    /// <summary>
    /// The check every guarded write in this module makes before it changes
    /// anything. It is here rather than in <c>EntityTags</c> because that is the
    /// Api's, and an act does not know there is an HTTP header.
    /// </summary>
    internal static void Current(ContentVersion current, ContentVersion held, string what)
    {
        if (!current.Matches(held))
        {
            throw Refusal.Stale(
                $"{what} has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.",
                current);
        }
    }
}

/// <summary>
/// Renames a folder, moves it, or both — with everything in it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A folder cannot be put inside itself.</strong> That is the one thing
/// a tree can be asked to do that would leave it unable to answer anything: the
/// subtree would be unreachable from the root and every walk of it would never
/// end. It is a <c>conflict</c> and not a <c>validation</c>, because the request
/// is well formed and it is the tree's current shape that refuses it.
/// </para>
/// <para>
/// Moving a folder moves what is in it without touching a row of it. The
/// children point at their parent and the parent is what changed
/// (<c>FolderConfiguration</c>): a stored path would have to be rewritten for
/// every descendant, and the first time that half-failed the tree would
/// disagree with itself.
/// </para>
/// </remarks>
public sealed class MoveOrRenameAFolder(
    ReachingAnApplication reaching,
    IStoredFiles files,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task<TheFolder> ExecuteAsync(
        Guid id, string? name, Guid? parent, ContentVersion held, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Files, cancellationToken);

        var folder = await TheFiles.FolderAsync(files, retention, id, cancellationToken);

        MoveOrRenameAFile.Current(folder.Version, held, "The folder");

        var accepted = FileName.Accepted(name);
        var subtree = await files.SubtreeAsync(id, cancellationToken);

        if (parent is { } destination && subtree.Any(beneath => beneath.Id == destination))
        {
            throw Refusal.Conflict(
                destination == id
                    ? $"A folder cannot be put inside itself. `{folder.Name}` is where it already is."
                    : $"`{folder.Name}` cannot be put inside something that is already in it.");
        }

        await TheFiles.RoomAtAsync(files, retention, parent, Height(subtree, id), cancellationToken);
        await TheFiles.NameIsFreeAsync(files, parent, accepted, id, cancellationToken);

        if (folder.Change(accepted, parent, clock.GetUtcNow()))
        {
            await files.SaveAsync("The folder", cancellationToken);
        }

        return TheFolder.Of(folder);
    }

    /// <summary>
    /// How many levels there are below <paramref name="root"/> — what the folder
    /// brings with it when it lands somewhere else.
    /// </summary>
    private static int Height(IReadOnlyList<Folder> subtree, Guid root)
    {
        var depths = new Dictionary<Guid, int> { [root] = 0 };
        var deepest = 0;

        // The subtree comes out of a breadth-first walk from the root, so a
        // folder's parent has always been seen before the folder itself.
        foreach (var folder in subtree.Skip(1))
        {
            var depth = folder.ParentId is { } parent && depths.TryGetValue(parent, out var above)
                ? above + 1
                : 1;

            depths[folder.Id] = depth;
            deepest = Math.Max(deepest, depth);
        }

        return deepest;
    }
}

/// <summary>
/// Sets a file aside. It leaves every ordinary read and can be brought back
/// until its retention runs out.
/// </summary>
/// <remarks>
/// <strong>This is the opposite of the Scratchpad's delete</strong>, and it is
/// what the Trash was built for an epic ago (<c>docs/api.md</c>, Deleting sets
/// content aside). An agent with write access may do it — that is what write
/// access is — and what an agent may not do is take it out of the Trash for
/// good, which is the owner's alone.
/// </remarks>
public sealed class DiscardAFile(
    ReachingAnApplication reaching,
    IStoredFiles files,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task ExecuteAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var who = await reaching.ToWriteAsync(WorkspaceApplication.Files, cancellationToken);
        var file = await TheFiles.FileAsync(files, retention, id, cancellationToken);

        MoveOrRenameAFile.Current(file.Version, held, "The file");

        await files.DeleteAsync(file, who, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>
/// Sets a folder aside, with everything in it, under one moment.
/// </summary>
/// <remarks>
/// One moment for the whole subtree is what makes it one Trash entry and what
/// brings it all back together (<see cref="Restoration"/>). Something below it
/// that the owner had already deleted separately keeps its own moment and its
/// own expiry: it does not come back when its folder does, and removing the
/// folder for good does not destroy it either.
/// </remarks>
public sealed class DiscardAFolder(
    ReachingAnApplication reaching,
    IStoredFiles files,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task ExecuteAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var who = await reaching.ToWriteAsync(WorkspaceApplication.Files, cancellationToken);
        var folder = await TheFiles.FolderAsync(files, retention, id, cancellationToken);

        MoveOrRenameAFile.Current(folder.Version, held, "The folder");

        await files.DeleteAsync(folder, who, clock.GetUtcNow(), cancellationToken);
    }
}
