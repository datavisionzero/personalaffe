using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Tasks;

namespace Personalaffe.Application.Acts.Tasks;

/// <summary>One list, and how much is in it.</summary>
public sealed record TheTaskList(
    Guid Id, string Name, int Open, int All, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static TheTaskList Of(TheList list) => new(
        list.List.Id, list.List.Name, list.Open, list.All, list.List.CreatedAt, list.List.UpdatedAt);

    internal static TheTaskList Of(TaskList list, int open, int all) => new(
        list.Id, list.Name, open, all, list.CreatedAt, list.UpdatedAt);
}

/// <summary>One task, as a caller sees it.</summary>
/// <param name="DueOn">
/// The day it is due, or nothing. A date and never a moment: the fourteenth is
/// the fourteenth wherever the owner is standing.
/// </param>
/// <param name="After">
/// The task it sits behind in its list, or nothing when it is first. It is
/// answered rather than a position, because a position is the module's
/// arithmetic and a neighbour is what a caller can act on.
/// </param>
public sealed record TheTask(
    Guid Id,
    Guid List,
    string Title,
    string Description,
    DateOnly? DueOn,
    bool Completed,
    DateTimeOffset? CompletedAt,
    Guid? After,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static TheTask Of(PersonalTask task, Guid? after) => new(
        task.Id,
        task.ListId,
        task.Title,
        task.Description,
        task.DueOn,
        task.Completed,
        task.CompletedAt,
        after,
        task.CreatedAt,
        task.UpdatedAt);
}

/// <summary>
/// What the Tasks acts have in common, and nothing more.
/// </summary>
internal static class TheTasks
{
    internal static Refusal NoSuchList(Guid id) =>
        Refusal.NotFound($"Nothing with the id {id} is a task list in this workspace.");

    internal static Refusal NoSuchTask(Guid id) =>
        Refusal.NotFound($"Nothing with the id {id} is a task in this workspace.");

    /// <summary>
    /// The list at <paramref name="id"/>, or the refusal its address answers.
    /// </summary>
    internal static async Task<TaskList> ListAsync(
        ITasks tasks, RetentionSettings retention, Guid id, CancellationToken cancellationToken)
    {
        var list = await tasks.FindListEvenDeletedAsync(id, cancellationToken) ?? throw NoSuchList(id);

        return list.IsDeleted()
            ? throw list.Gone($"The list `{list.Name}`", retention.Trash)
            : list;
    }

    /// <inheritdoc cref="ListAsync"/>
    internal static async Task<PersonalTask> TaskAsync(
        ITasks tasks, RetentionSettings retention, Guid id, CancellationToken cancellationToken)
    {
        var task = await tasks.FindEvenDeletedAsync(id, cancellationToken) ?? throw NoSuchTask(id);

        return task.IsDeleted()
            ? throw task.Gone($"The task `{task.Title}`", retention.Trash)
            : task;
    }

    internal static void RequireCurrent(ContentVersion current, ContentVersion held, string what)
    {
        if (!current.Matches(held))
        {
            throw Refusal.Stale(
                $"{what} has changed since it was read. Read it again: the write you sent would have "
                + $"replaced somebody else's newer one.",
                current);
        }
    }

    /// <summary>The task a given one sits behind, out of a list already read.</summary>
    internal static Guid? Behind(IReadOnlyList<PersonalTask> order, Guid id)
    {
        var at = order.ToList().FindIndex(task => task.Id == id);

        return at > 0 ? order[at - 1].Id : null;
    }

    /// <summary>
    /// Where something goes in <paramref name="list"/> when it is put behind
    /// <paramref name="after"/>, and whether the list has to be renumbered
    /// first.
    /// </summary>
    /// <remarks>
    /// The neighbours are what the caller named and what follows it; the
    /// position is the midpoint (<see cref="Positions"/>). A list with no room
    /// left between those two is renumbered by the caller and asked again —
    /// which is the one case where a move is a change to more than one row.
    /// </remarks>
    internal static (double? Position, bool Renumber) Placed(
        IReadOnlyList<PersonalTask> list, Guid? after, Guid? itself)
    {
        var others = list.Where(task => task.Id != itself).ToList();

        if (after is not { } behind)
        {
            var first = others.Count == 0 ? (double?)null : others[0].Position;

            return others.Count == 0
                ? (Positions.Between(null, null), false)
                : Room(null, first);
        }

        var at = others.FindIndex(task => task.Id == behind);

        if (at < 0)
        {
            throw Refusal.Validation(
                "after",
                "A task can only be put behind another task in the same list. That one is somewhere else, "
                + "or is not there at all.");
        }

        var above = others[at].Position;
        var below = at + 1 < others.Count ? others[at + 1].Position : (double?)null;

        return Room(above, below);
    }

    private static (double? Position, bool Renumber) Room(double? above, double? below)
    {
        if (above is { } one && below is { } other && !Positions.RoomBetween(one, other))
        {
            return (null, true);
        }

        return (Positions.Between(above, below), false);
    }
}
