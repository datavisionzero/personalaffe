using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The Files application's half of the Trash (<see cref="ITrash"/>) — the first
/// one that is not a test double.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A Trash entry is a deletion and not a row.</strong> Deleting a folder
/// sets its whole subtree aside under one moment, and what the owner sees in the
/// Trash is the folder: one thing they deleted, one thing they can bring back.
/// Something further down that they had deleted separately keeps its own moment
/// and is an entry of its own, with an expiry of its own — it does not come back
/// when its folder does, and removing the folder for good does not destroy it
/// (<see cref="Restoration"/>).
/// </para>
/// <para>
/// <strong>Rows first, then bytes.</strong> Every removal here deletes the
/// metadata and then the files, in that order and never the other way: a crash
/// between them leaves bytes nobody points at, which the tidy-up removes, where
/// the other order would leave a row whose file is missing. That is the same
/// trade the upload makes, in the same direction.
/// </para>
/// </remarks>
public sealed class FilesTrash(
    PersonalaffeDbContext context, IFileBytes bytes, RetentionSettings retention) : ITrash
{
    public WorkspaceApplication Application => WorkspaceApplication.Files;

    public async Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var (folders, files) = await EverythingAsync(cancellationToken);

        var entries = new List<TrashEntry>();

        foreach (var folder in folders.Where(folder => folder.IsDeleted() && IsItsOwnEntry(folders, folder)))
        {
            entries.Add(Entry(folder.Id, folder.Name, folder.ParentId, folders, folder));
        }

        foreach (var file in files.Where(file => file.IsDeleted() && IsItsOwnEntry(folders, file)))
        {
            entries.Add(Entry(file.Id, file.Name, file.FolderId, folders, file));
        }

        return
        [
            .. entries
                .OrderByDescending(entry => entry.DeletedAt)
                .ThenBy(entry => entry.Id)
                .Take(limit),
        ];
    }

    public async Task<RestoredTo?> RestoreAsync(
        Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken)
    {
        var (folders, files) = await EverythingAsync(cancellationToken);

        var folder = folders.FirstOrDefault(candidate => candidate.Id == id && candidate.IsDeleted());
        var file = files.FirstOrDefault(candidate => candidate.Id == id && candidate.IsDeleted());

        if (folder is null && file is null)
        {
            return null;
        }

        var itself = new Placement(
            id,
            folder?.Name ?? file!.Name,
            Deleted: true);

        var version = folder is not null ? folder.Version : file!.Version;

        // Spelled with `is not null` and not with `??`: the value is nullable
        // in its own right — a thing at the top of the tree is in no folder —
        // so `folder?.ParentId ?? file!.FolderId` would reach for the file
        // whenever the folder is at the top, and there is no file.
        var where = folder is not null ? folder.ParentId : file!.FolderId;

        RequireCurrent(version, held, folder is null ? "The file" : "The folder");

        var (above, reachesTheRoot) = Above(folders, where);
        var destination = reachesTheRoot ? where : null;

        var plan = Restoration.Plan(
            [itself, .. above],
            reachesTheRoot,
            restoreAs,
            [.. TakenIn(folders, files, destination, id)]);

        var now = DateTimeOffset.UtcNow;
        var went = folder is not null ? folder.DeletedAt : file!.DeletedAt;

        if (folder is not null)
        {
            // Everything that went with it comes back with it: the subtree
            // shares the moment it was deleted at, which is what makes "the
            // whole thing" a query rather than a guess. The moment is read
            // before anything is restored, because restoring clears it.
            var (beneathFolders, beneathFiles) = WentWith(folders, files, folder.Id, went);

            foreach (var beneath in beneathFolders)
            {
                beneath.Restore();
                beneath.Touch(now);
            }

            foreach (var beneath in beneathFiles)
            {
                beneath.Restore();
                beneath.Touch(now);
            }
        }

        // An ancestor comes back because the thing would otherwise be
        // unreachable, and for no other reason — so it comes back as itself and
        // not with everything it used to contain.
        foreach (var ancestor in folders.Where(
                     candidate => candidate.Id != id && plan.Restore.Contains(candidate.Id)))
        {
            ancestor.Restore();
            ancestor.Touch(now);
        }

        if (folder is not null)
        {
            folder.Restore();
            folder.Change(plan.Name, plan.Parent, now);
            folder.Touch(now);
        }
        else if (file is not null)
        {
            file.Restore();
            file.Change(plan.Name, plan.Parent, now);
            file.Touch(now);
        }

        await GuardedSave.SaveAsync(context, folder is null ? "The file" : "The folder", cancellationToken);

        return new RestoredTo(
            WorkspaceApplication.Files,
            id,
            plan.Name,
            plan.Parent is { } parent
                ? folders.First(candidate => candidate.Id == parent).Name
                : null,
            plan.MovedToTheRoot);
    }

    public async Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var (folders, files) = await EverythingAsync(cancellationToken);

        var folder = folders.FirstOrDefault(candidate => candidate.Id == id && candidate.IsDeleted());
        var file = files.FirstOrDefault(candidate => candidate.Id == id && candidate.IsDeleted());

        if (folder is null && file is null)
        {
            return false;
        }

        RequireCurrent(
            folder is not null ? folder.Version : file!.Version,
            held,
            folder is null ? "The file" : "The folder");

        if (file is not null)
        {
            await RemoveAsync([file], [], cancellationToken);
            return true;
        }

        // What goes is this Trash entry: the folder, and what was deleted with
        // it. Something below it that the owner deleted separately is an entry
        // of its own, and destroying it as a side effect would destroy
        // something nobody selected.
        var (goingFolders, goingFiles) = WentWith(folders, files, folder!.Id, folder.DeletedAt);

        await RemoveAsync(goingFiles, goingFolders, cancellationToken);

        return true;
    }

    public async Task<int> EmptyAsync(CancellationToken cancellationToken)
    {
        var (folders, files) = await EverythingAsync(cancellationToken);

        return await RemoveAsync(
            [.. files.Where(file => file.IsDeleted())],
            [.. folders.Where(folder => folder.IsDeleted())],
            cancellationToken);
    }

    public async Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken)
    {
        var (folders, files) = await EverythingAsync(cancellationToken);

        return await RemoveAsync(
            [.. files.Where(file => file.DeletedAt is { } at && at <= expiredBefore)],
            [.. folders.Where(folder => folder.DeletedAt is { } at && at <= expiredBefore)],
            cancellationToken);
    }

    /// <summary>
    /// Removes rows and then the bytes behind them, and says how many things
    /// went.
    /// </summary>
    /// <remarks>
    /// The bytes go after the rows have been committed, and a file whose bytes
    /// are already missing is not a failure: the sweep has to be able to finish
    /// a job it half-finished before the instance was killed, and one that threw
    /// on the first missing file would never reach the second.
    /// </remarks>
    private async Task<int> RemoveAsync(
        IReadOnlyList<StoredFile> files,
        IReadOnlyList<Folder> folders,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0 && folders.Count == 0)
        {
            return 0;
        }

        context.Files.RemoveRange(files);
        context.Folders.RemoveRange(folders);

        await context.SaveChangesAsync(cancellationToken);

        foreach (var file in files)
        {
            await bytes.RemoveAsync(file.Id, cancellationToken);
        }

        return files.Count + folders.Count;
    }

    /// <summary>
    /// Everything the two tables hold, the Trash included. It is one read
    /// because every question this class asks is about the shape of the tree,
    /// and a personal file area is small enough that walking it in memory is
    /// the simplest thing that is also fast.
    /// </summary>
    private async Task<(List<Folder> Folders, List<StoredFile> Files)> EverythingAsync(
        CancellationToken cancellationToken) =>
        (await context.Folders.IgnoreQueryFilters().ToListAsync(cancellationToken),
         await context.Files.IgnoreQueryFilters().ToListAsync(cancellationToken));

    /// <summary>
    /// Whether this is something the owner deleted rather than something that
    /// went with what they deleted.
    /// </summary>
    private static bool IsItsOwnEntry(IReadOnlyList<Folder> folders, IRecoverable thing)
    {
        var parentId = thing switch
        {
            Folder folder => folder.ParentId,
            StoredFile file => file.FolderId,
            _ => null,
        };

        if (parentId is not { } id)
        {
            return true;
        }

        var parent = folders.FirstOrDefault(candidate => candidate.Id == id);

        // No parent left at all means the folder it was in has been removed for
        // good while this was in the Trash: it is certainly nobody else's
        // entry now.
        return parent is null || parent.DeletedAt != thing.DeletedAt;
    }

    /// <summary>
    /// The folders from <paramref name="from"/> up to the root, and whether the
    /// chain reaches it. It does not when an ancestor expired out of the Trash
    /// and was removed for good.
    /// </summary>
    private static (List<Placement> Chain, bool ReachesTheRoot) Above(
        IReadOnlyList<Folder> folders, Guid? from)
    {
        var chain = new List<Placement>();
        var walking = from;

        while (walking is { } id)
        {
            if (folders.FirstOrDefault(candidate => candidate.Id == id) is not { } folder)
            {
                return (chain, false);
            }

            chain.Add(new Placement(folder.Id, folder.Name, folder.IsDeleted()));
            walking = folder.ParentId;
        }

        return (chain, true);
    }

    /// <summary>
    /// A folder's subtree, folders and files, restricted to what was deleted at
    /// the same moment it was — which is what "what went with it" means.
    /// </summary>
    private static (List<Folder> Folders, List<StoredFile> Files) WentWith(
        IReadOnlyList<Folder> folders,
        IReadOnlyList<StoredFile> files,
        Guid root,
        DateTimeOffset? went)
    {
        var subtree = StoredFiles.Subtree(folders, root);
        var ids = subtree.Select(folder => folder.Id).ToHashSet();

        return (
            [.. subtree.Where(folder => folder.DeletedAt == went)],
            [.. files.Where(file => file.FolderId is { } id && ids.Contains(id) && file.DeletedAt == went)]);
    }

    /// <summary>The live names in one folder, other than the thing coming back.</summary>
    private static IEnumerable<string> TakenIn(
        IReadOnlyList<Folder> folders, IReadOnlyList<StoredFile> files, Guid? folder, Guid itself) =>
        folders
            .Where(one => one.ParentId == folder && !one.IsDeleted() && one.Id != itself)
            .Select(one => one.Name)
            .Concat(files
                .Where(one => one.FolderId == folder && !one.IsDeleted() && one.Id != itself)
                .Select(one => one.Name));

    private TrashEntry Entry(
        Guid id, string name, Guid? where, IReadOnlyList<Folder> folders, IRecoverable thing)
    {
        var deletedAt = thing.DeletedAt!.Value;

        return new TrashEntry(
            WorkspaceApplication.Files,
            id,
            name,
            where is { } parent
                ? folders.FirstOrDefault(candidate => candidate.Id == parent)?.Name
                : null,
            deletedAt,
            thing.DeletedBy!,
            deletedAt + retention.Trash,
            thing switch
            {
                Folder folder => folder.UpdatedAt,
                StoredFile file => file.UpdatedAt,
                _ => deletedAt,
            });
    }

    private static void RequireCurrent(ContentVersion current, ContentVersion held, string what)
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
