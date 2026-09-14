using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The Files application's metadata in Postgres (<see cref="IStoredFiles"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The tree is walked in memory.</strong> A chain from a folder to the
/// root is at most <see cref="Folder.MaxDepth"/> rows and a listing is one
/// folder's children; a recursive CTE would buy nothing here and would put the
/// shape of the hierarchy into SQL, where the module could no longer say what
/// it is. This is one person's file area, not a filesystem.
/// </para>
/// <para>
/// Every ordinary read goes through the query filter <c>IsRecoverable</c>
/// installs, so a deleted row is absent without anybody remembering to exclude
/// it. The three reads that want them say <c>IgnoreQueryFilters</c> out loud.
/// </para>
/// </remarks>
public sealed class StoredFiles(PersonalaffeDbContext context) : IStoredFiles
{
    public async Task<TheContents> ReadAsync(Guid? folder, CancellationToken cancellationToken)
    {
        var chain = Array.Empty<Folder>() as IReadOnlyList<Folder>;

        if (folder is { } id)
        {
            var (walked, _) = await ChainAsync(id, cancellationToken);

            // The breadcrumb reads from the root down, and the chain is built
            // from the folder up. Reversing here means every caller — a screen,
            // the CLI, a test — gets it in the order a person reads it.
            chain = [.. walked.AsEnumerable().Reverse()];
        }

        var folders = await context.Folders
            .Where(candidate => candidate.ParentId == folder)
            .OrderBy(candidate => candidate.Name)
            .ToListAsync(cancellationToken);

        var files = await context.Files
            .Where(candidate => candidate.FolderId == folder)
            .OrderBy(candidate => candidate.Name)
            .ToListAsync(cancellationToken);

        return new TheContents(chain, folders, files);
    }

    public async Task<StoredFile?> FindFileAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Files.FirstOrDefaultAsync(file => file.Id == id, cancellationToken);

    public async Task<Folder?> FindFolderAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Folders.FirstOrDefaultAsync(folder => folder.Id == id, cancellationToken);

    public async Task<StoredFile?> FindFileEvenDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Files.IgnoreQueryFilters().FirstOrDefaultAsync(file => file.Id == id, cancellationToken);

    public async Task<Folder?> FindFolderEvenDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Folders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(folder => folder.Id == id, cancellationToken);

    public async Task<long> StoredBytesAsync(CancellationToken cancellationToken) =>
        await context.Files.IgnoreQueryFilters().SumAsync(file => file.Size, cancellationToken);

    public async Task<bool> NameIsTakenAsync(
        Guid? folder, string name, Guid? itself, CancellationToken cancellationToken)
    {
        // Compared in the database with the collation the column has, then
        // confirmed here with the product's own rule. Postgres's default
        // collation is case-sensitive, so the query is a coarse filter and
        // FileName.Same is the answer — one place decides what "the same name"
        // means, and it is Domain.
        var folders = await context.Folders
            .Where(candidate => candidate.ParentId == folder && candidate.Id != itself)
            .Select(candidate => candidate.Name)
            .ToListAsync(cancellationToken);

        var files = await context.Files
            .Where(candidate => candidate.FolderId == folder && candidate.Id != itself)
            .Select(candidate => candidate.Name)
            .ToListAsync(cancellationToken);

        return folders.Concat(files).Any(taken => FileName.Same(taken, name));
    }

    public async Task<(IReadOnlyList<Folder> Chain, bool ReachesTheRoot)> ChainAsync(
        Guid folder, CancellationToken cancellationToken)
    {
        var chain = new List<Folder>();
        var walking = await FindFolderEvenDeletedAsync(folder, cancellationToken);

        while (walking is not null)
        {
            chain.Add(walking);

            if (walking.ParentId is not { } parentId)
            {
                return (chain, true);
            }

            // A depth this product cannot produce means the tree has a cycle in
            // it, which nothing can make and a bug could. Stopping is what keeps
            // one bad row from being an infinite loop in a request.
            if (chain.Count > Folder.MaxDepth)
            {
                throw new InvalidOperationException(
                    $"The folder {folder} is deeper than {Folder.MaxDepth} folders, which nothing can make.");
            }

            walking = await FindFolderEvenDeletedAsync(parentId, cancellationToken);
        }

        // The walk stopped at a parent that is not there: an ancestor expired
        // out of the Trash and was removed for good. Restoration knows what that
        // means; the walk only has to say it happened.
        return (chain, false);
    }

    public async Task AddAsync(StoredFile file, CancellationToken cancellationToken)
    {
        await context.Files.AddAsync(file, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(Folder folder, CancellationToken cancellationToken)
    {
        await context.Folders.AddAsync(folder, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(string what, CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, what, cancellationToken);

    public Task DeleteAsync(
        StoredFile file, Caller by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        file.Delete(by, at);
        file.Touch(at);

        return GuardedSave.SaveAsync(context, "The file", cancellationToken);
    }

    public async Task DeleteAsync(
        Folder folder, Caller by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // Everything under it goes with it, under one moment, so that the whole
        // thing is one Trash entry and comes back together (Restoration). The
        // moment is `at` for every row, which is what makes "what went with it"
        // a query rather than a guess.
        var folders = await context.Folders.ToListAsync(cancellationToken);
        var files = await context.Files.ToListAsync(cancellationToken);

        foreach (var beneath in Subtree(folders, folder.Id))
        {
            beneath.Delete(by, at);
            beneath.Touch(at);
        }

        var going = Subtree(folders, folder.Id).Select(one => one.Id).ToHashSet();

        foreach (var file in files.Where(file => file.FolderId is { } id && going.Contains(id)))
        {
            file.Delete(by, at);
            file.Touch(at);
        }

        await GuardedSave.SaveAsync(context, "The folder", cancellationToken);
    }

    /// <summary>
    /// A folder and everything under it, out of a list somebody has already
    /// read. Which list decides what "under it" means: the live folders when
    /// something is being deleted, and every folder when something is coming
    /// back.
    /// </summary>
    internal static List<Folder> Subtree(IReadOnlyList<Folder> all, Guid root)
    {
        var found = new List<Folder>();
        var pending = new Queue<Guid>([root]);

        while (pending.TryDequeue(out var id))
        {
            if (all.FirstOrDefault(folder => folder.Id == id) is not { } folder)
            {
                continue;
            }

            found.Add(folder);

            foreach (var child in all.Where(candidate => candidate.ParentId == id))
            {
                pending.Enqueue(child.Id);
            }
        }

        return found;
    }
}
