using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Tasks;

namespace Personalaffe.Application.Acts.Tasks;

/// <summary>The lists, by name, with how much is open in each.</summary>
public sealed class ReadTheLists(ReachingAnApplication reaching, ITasks tasks)
{
    public async Task<IReadOnlyList<TheTaskList>> ExecuteAsync(CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Tasks, cancellationToken);

        return [.. (await tasks.ListsAsync(cancellationToken)).Select(TheTaskList.Of)];
    }
}

/// <summary>A list made now.</summary>
public sealed class MakeAList(ReachingAnApplication reaching, ITasks tasks, TimeProvider clock)
{
    public async Task<TheTaskList> ExecuteAsync(string? name, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Tasks, cancellationToken);

        var accepted = TaskTitle.Accepted(name, "name");

        await Free(tasks, accepted, itself: null, cancellationToken);

        var list = TaskList.Named(accepted, clock.GetUtcNow());

        await tasks.AddAsync(list, cancellationToken);

        return TheTaskList.Of(list, open: 0, all: 0);
    }

    /// <summary>
    /// Whether a list is already called that.
    /// </summary>
    /// <remarks>
    /// Two lists with one name is a workspace where "put it in Einkauf" means
    /// nothing, and where the console cannot resolve a name at all. It is a
    /// <c>conflict</c> and never a silent rename, as every other name in this
    /// product is.
    /// </remarks>
    internal static async Task Free(
        ITasks tasks, string name, Guid? itself, CancellationToken cancellationToken)
    {
        if (await tasks.NameIsTakenAsync(name, itself, cancellationToken))
        {
            throw Refusal.Conflict(
                $"A list is already called `{name}`. Names are one each, whatever their capitals.");
        }
    }
}

/// <summary>Renames a list.</summary>
public sealed class RenameAList(
    ReachingAnApplication reaching, ITasks tasks, RetentionSettings retention, TimeProvider clock)
{
    public async Task<TheTaskList> ExecuteAsync(
        Guid id, string? name, ContentVersion held, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Tasks, cancellationToken);

        var list = await TheTasks.ListAsync(tasks, retention, id, cancellationToken);

        TheTasks.RequireCurrent(list.Version, held, "The list");

        var accepted = TaskTitle.Accepted(name, "name");

        await MakeAList.Free(tasks, accepted, id, cancellationToken);

        if (list.Rename(accepted, clock.GetUtcNow()))
        {
            await tasks.SaveAsync("The list", cancellationToken);
        }

        var counted = (await tasks.ListsAsync(cancellationToken)).First(one => one.List.Id == id);

        return TheTaskList.Of(counted);
    }
}

/// <summary>
/// Sets a list aside, with every task in it.
/// </summary>
/// <remarks>
/// One moment for the whole list, so it is one Trash entry and comes back
/// together. A task the owner had already deleted on its own keeps its own
/// moment and its own entry.
/// </remarks>
public sealed class DiscardAList(
    ReachingAnApplication reaching, ITasks tasks, RetentionSettings retention, TimeProvider clock)
{
    public async Task ExecuteAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var who = await reaching.ToWriteAsync(WorkspaceApplication.Tasks, cancellationToken);
        var list = await TheTasks.ListAsync(tasks, retention, id, cancellationToken);

        TheTasks.RequireCurrent(list.Version, held, "The list");

        await tasks.DeleteAsync(list, who, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>What is in a list, in the owner's order.</summary>
/// <remarks>
/// <strong>Open and completed together, in one order.</strong> Separating them
/// is what a client draws (VISION §6.4), and doing it here would mean a caller
/// could not put a task back where it was after reopening it.
/// </remarks>
public sealed class ReadTheTasks(
    ReachingAnApplication reaching, ITasks tasks, RetentionSettings retention)
{
    public async Task<IReadOnlyList<TheTask>> ExecuteAsync(Guid list, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Tasks, cancellationToken);
        await TheTasks.ListAsync(tasks, retention, list, cancellationToken);

        var order = await tasks.TasksAsync(list, cancellationToken);

        return [.. order.Select((task, at) => TheTask.Of(task, at > 0 ? order[at - 1].Id : null))];
    }
}

/// <summary>One task, by its own address.</summary>
public sealed class ReadATask(
    ReachingAnApplication reaching, ITasks tasks, RetentionSettings retention)
{
    public async Task<TheTask> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Tasks, cancellationToken);

        var task = await TheTasks.TaskAsync(tasks, retention, id, cancellationToken);
        var order = await tasks.TasksAsync(task.ListId, cancellationToken);

        return TheTask.Of(task, TheTasks.Behind(order, id));
    }
}

/// <summary>
/// Captures a task at the end of a list.
/// </summary>
/// <remarks>
/// At the end and not at the top, because capture is what happens when
/// something occurs to somebody and the order is what they decide afterwards.
/// A new task jumping the queue would reorder the list every time they think of
/// something.
/// </remarks>
public sealed class CaptureATask(
    ReachingAnApplication reaching, ITasks tasks, RetentionSettings retention, TimeProvider clock)
{
    public async Task<TheTask> ExecuteAsync(
        Guid list,
        string? title,
        string? description,
        DateOnly? dueOn,
        CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Tasks, cancellationToken);
        await TheTasks.ListAsync(tasks, retention, list, cancellationToken);

        var order = await tasks.TasksAsync(list, cancellationToken);
        var last = order.Count == 0 ? (double?)null : order[^1].Position;

        var task = PersonalTask.Captured(
            list, title, description, dueOn, Positions.Between(last, null), clock.GetUtcNow());

        await tasks.AddAsync(task, cancellationToken);

        return TheTask.Of(task, order.Count == 0 ? null : order[^1].Id);
    }
}

/// <summary>
/// Changes anything about a task: its title, its description, its due date, its
/// list, whether it is done, and where it sits.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One write and not six.</strong> A task has more fields than anything
/// else in this workspace and is exactly where a second address per field would
/// start to look reasonable — `POST .../complete`, `PUT .../due`,
/// `POST .../move`. Each would be another place the guard has to be got right,
/// and the owner who ticks a box and changes the date has made one change.
/// </para>
/// <para>
/// <strong>Where it sits is a neighbour and not a number.</strong> The caller
/// says which task it goes behind — or nothing, for the top of the list — and
/// the arithmetic is the module's (<see cref="Positions"/>). A caller that is
/// not moving anything sends the neighbour it already has, which both clients
/// do because they read first.
/// </para>
/// </remarks>
public sealed class ChangeATask(
    ReachingAnApplication reaching, ITasks tasks, RetentionSettings retention, TimeProvider clock)
{
    public async Task<TheTask> ExecuteAsync(
        Guid id,
        Guid list,
        string? title,
        string? description,
        DateOnly? dueOn,
        bool completed,
        Guid? after,
        ContentVersion held,
        CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Tasks, cancellationToken);

        var task = await TheTasks.TaskAsync(tasks, retention, id, cancellationToken);

        TheTasks.RequireCurrent(task.Version, held, "The task");

        await TheTasks.ListAsync(tasks, retention, list, cancellationToken);

        var now = clock.GetUtcNow();
        var order = await tasks.TasksAsync(list, cancellationToken);
        var (position, renumber) = TheTasks.Placed(order, after, id);

        if (renumber)
        {
            // The one case where a move is a change to more than one row: the
            // gap between two neighbours has run out of numbers. About fifty
            // moves into the same place gets here.
            await RenumberAsync(order, id, now, cancellationToken);

            order = await tasks.TasksAsync(list, cancellationToken);
            (position, _) = TheTasks.Placed(order, after, id);
        }

        if (task.Change(list, title, description, dueOn, completed, position!.Value, now))
        {
            await tasks.SaveAsync("The task", cancellationToken);
        }

        var settled = await tasks.TasksAsync(list, cancellationToken);

        return TheTask.Of(task, TheTasks.Behind(settled, id));
    }

    private async Task RenumberAsync(
        IReadOnlyList<PersonalTask> order, Guid moving, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var staying = order.Where(task => task.Id != moving).ToList();
        var spaced = Positions.Renumbered(staying.Count);

        for (var at = 0; at < staying.Count; at++)
        {
            staying[at].PutAt(spaced[at], now);
        }

        await tasks.SaveAsync("The list", cancellationToken);
    }
}

/// <summary>Sets a task aside. It comes back at the end of its list.</summary>
public sealed class DiscardATask(
    ReachingAnApplication reaching, ITasks tasks, RetentionSettings retention, TimeProvider clock)
{
    public async Task ExecuteAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var who = await reaching.ToWriteAsync(WorkspaceApplication.Tasks, cancellationToken);
        var task = await TheTasks.TaskAsync(tasks, retention, id, cancellationToken);

        TheTasks.RequireCurrent(task.Version, held, "The task");

        await tasks.DeleteAsync(task, who, clock.GetUtcNow(), cancellationToken);
    }
}
