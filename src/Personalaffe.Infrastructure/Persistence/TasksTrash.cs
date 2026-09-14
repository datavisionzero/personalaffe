using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Tasks;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// Tasks' half of the Trash (<see cref="ITrash"/>), and the third and last
/// contributor.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the one that does not apply <see cref="Restoration"/>.</strong>
/// There is no tree here: a list is not in another list, and a task is in a
/// list. So there is no ancestor to bring back with anything, and the only
/// question a restore has is the one every restore has — is the name free where
/// it is going. A module with nothing to restore <em>into</em> does not need the
/// rules for restoring into a tree, and pretending otherwise would be applying
/// a convention for the look of it.
/// </para>
/// <para>
/// A deleted list is one entry however many tasks went with it, as a deleted
/// folder and a deleted page are. A task the owner deleted on its own keeps its
/// own moment and its own entry.
/// </para>
/// </remarks>
public sealed class TasksTrash(
    PersonalaffeDbContext context, RetentionSettings retention) : ITrash
{
    public WorkspaceApplication Application => WorkspaceApplication.Tasks;

    public async Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var (lists, tasks) = await EverythingAsync(cancellationToken);

        var entries = new List<TrashEntry>();

        foreach (var list in lists.Where(list => list.IsDeleted()))
        {
            entries.Add(Entry(list.Id, list.Name, where: null, list.DeletedAt!.Value, list.DeletedBy!, list.UpdatedAt));
        }

        foreach (var task in tasks.Where(task => task.IsDeleted() && IsItsOwnEntry(lists, task)))
        {
            entries.Add(Entry(
                task.Id,
                task.Title,
                lists.FirstOrDefault(list => list.Id == task.ListId)?.Name,
                task.DeletedAt!.Value,
                task.DeletedBy!,
                task.UpdatedAt));
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
        var (lists, tasks) = await EverythingAsync(cancellationToken);

        if (lists.FirstOrDefault(list => list.Id == id && list.IsDeleted()) is { } list)
        {
            return await RestoreAsync(list, lists, tasks, held, restoreAs, cancellationToken);
        }

        if (tasks.FirstOrDefault(task => task.Id == id && task.IsDeleted()) is not { } task)
        {
            return null;
        }

        RequireCurrent(task.Version, held, "The task");

        var now = DateTimeOffset.UtcNow;
        var itsList = lists.FirstOrDefault(one => one.Id == task.ListId);

        // The list it was in may have been removed for good while the task sat
        // in the Trash. A task with no list is a task nobody can reach, so it
        // comes back in the first list there is — and the caller is told, as
        // `Restoration` would have told them about a folder.
        var landing = itsList is { } found && !found.IsDeleted()
            ? found
            : lists.FirstOrDefault(one => !one.IsDeleted())
              ?? throw Refusal.Conflict(
                  "There is no list to put this back in. Make one first, and then restore it.");

        var moved = landing.Id != task.ListId;

        task.Restore();
        task.Change(
            landing.Id,
            Renamed(restoreAs, task.Title),
            task.Description,
            task.DueOn,
            task.Completed,
            await EndOfAsync(landing.Id, cancellationToken),
            now);
        task.Touch(now);

        await GuardedSave.SaveAsync(context, "The task", cancellationToken);

        return new RestoredTo(
            WorkspaceApplication.Tasks, id, task.Title, landing.Name, MovedToTheRoot: moved);
    }

    public async Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var (lists, tasks) = await EverythingAsync(cancellationToken);

        if (lists.FirstOrDefault(list => list.Id == id && list.IsDeleted()) is { } list)
        {
            RequireCurrent(list.Version, held, "The list");

            // What goes is this Trash entry: the list, and what was deleted with
            // it. A task the owner deleted separately is an entry of its own.
            await RemoveAsync(
                [list],
                [.. tasks.Where(task => task.ListId == list.Id && task.DeletedAt == list.DeletedAt)],
                cancellationToken);

            return true;
        }

        if (tasks.FirstOrDefault(task => task.Id == id && task.IsDeleted()) is not { } itself)
        {
            return false;
        }

        RequireCurrent(itself.Version, held, "The task");

        await RemoveAsync([], [itself], cancellationToken);

        return true;
    }

    public async Task<int> EmptyAsync(CancellationToken cancellationToken)
    {
        var (lists, tasks) = await EverythingAsync(cancellationToken);

        return await RemoveAsync(
            [.. lists.Where(list => list.IsDeleted())],
            [.. tasks.Where(task => task.IsDeleted())],
            cancellationToken);
    }

    public async Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken)
    {
        var (lists, tasks) = await EverythingAsync(cancellationToken);

        return await RemoveAsync(
            [.. lists.Where(list => list.DeletedAt is { } at && at <= expiredBefore)],
            [.. tasks.Where(task => task.DeletedAt is { } at && at <= expiredBefore)],
            cancellationToken);
    }

    private async Task<RestoredTo> RestoreAsync(
        TaskList list,
        IReadOnlyList<TaskList> lists,
        IReadOnlyList<PersonalTask> tasks,
        ContentVersion held,
        string? restoreAs,
        CancellationToken cancellationToken)
    {
        RequireCurrent(list.Version, held, "The list");

        var name = Renamed(restoreAs, list.Name);

        if (lists.Any(other => other.Id != list.Id && !other.IsDeleted() && TaskTitle.Same(other.Name, name)))
        {
            throw Refusal.Conflict(
                $"A list is already called `{name}`. Restore it under another name, or rename the one "
                + "that is there.");
        }

        var now = DateTimeOffset.UtcNow;
        var went = list.DeletedAt;

        foreach (var task in tasks.Where(task => task.ListId == list.Id && task.DeletedAt == went))
        {
            task.Restore();
            task.Touch(now);
        }

        list.Restore();
        list.Rename(name, now);
        list.Touch(now);

        await GuardedSave.SaveAsync(context, "The list", cancellationToken);

        return new RestoredTo(WorkspaceApplication.Tasks, list.Id, name, null, MovedToTheRoot: false);
    }

    private async Task<int> RemoveAsync(
        IReadOnlyList<TaskList> lists,
        IReadOnlyList<PersonalTask> tasks,
        CancellationToken cancellationToken)
    {
        if (lists.Count == 0 && tasks.Count == 0)
        {
            return 0;
        }

        context.Tasks.RemoveRange(tasks);
        context.TaskLists.RemoveRange(lists);

        await context.SaveChangesAsync(cancellationToken);

        return lists.Count + tasks.Count;
    }

    /// <summary>Where a task goes when it is put back: the end of its list.</summary>
    private async Task<double> EndOfAsync(Guid list, CancellationToken cancellationToken)
    {
        var last = await context.Tasks
            .Where(task => task.ListId == list)
            .OrderByDescending(task => task.Position)
            .Select(task => (double?)task.Position)
            .FirstOrDefaultAsync(cancellationToken);

        return Positions.Between(last, null);
    }

    private async Task<(List<TaskList> Lists, List<PersonalTask> Tasks)> EverythingAsync(
        CancellationToken cancellationToken) =>
        (await context.TaskLists.IgnoreQueryFilters().ToListAsync(cancellationToken),
         await context.Tasks.IgnoreQueryFilters().ToListAsync(cancellationToken));

    /// <summary>
    /// Whether this is something the owner deleted rather than something that
    /// went with the list it was in.
    /// </summary>
    private static bool IsItsOwnEntry(IReadOnlyList<TaskList> lists, PersonalTask task)
    {
        var list = lists.FirstOrDefault(one => one.Id == task.ListId);

        return list is null || list.DeletedAt != task.DeletedAt;
    }

    private TrashEntry Entry(
        Guid id, string name, string? where, DateTimeOffset deletedAt, Actor by, DateTimeOffset updatedAt) =>
        new(
            WorkspaceApplication.Tasks,
            id,
            name,
            where,
            deletedAt,
            by,
            deletedAt + retention.Trash,
            updatedAt);

    private static string Renamed(string? restoreAs, string itsOwn) =>
        restoreAs is null ? itsOwn : TaskTitle.Accepted(restoreAs, "name");

    private static void RequireCurrent(ContentVersion current, ContentVersion held, string what)
    {
        if (!current.Matches(held))
        {
            throw Refusal.Stale(
                $"{what} has changed since it was read. Read it again: the write you sent would have "
                + $"replaced somebody else's newer one.",
                current);
        }
    }
}
