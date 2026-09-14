using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Tasks;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The task lists and their tasks in Postgres (<see cref="ITasks"/>).
/// </summary>
/// <remarks>
/// The simplest store in the product: two tables, no tree to walk, and no bytes
/// beside the rows. What is worth reading is how the counts are taken — one
/// grouped query rather than a listing per list — and that a listing is ordered
/// by <c>position</c>, which is the index it has.
/// </remarks>
public sealed class Tasks(PersonalaffeDbContext context) : ITasks
{
    public async Task<IReadOnlyList<TheList>> ListsAsync(CancellationToken cancellationToken)
    {
        var lists = await context.TaskLists
            .OrderBy(list => list.Name)
            .ToListAsync(cancellationToken);

        // One query for every count, rather than one per list: what a client
        // draws first is which list has anything in it.
        var counted = await context.Tasks
            .GroupBy(task => task.ListId)
            .Select(group => new
            {
                List = group.Key,
                All = group.Count(),
                Open = group.Count(task => task.CompletedAt == null),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. lists.Select(list =>
            {
                var counts = counted.FirstOrDefault(one => one.List == list.Id);

                return new TheList(list, counts?.Open ?? 0, counts?.All ?? 0);
            }),
        ];
    }

    public async Task<TaskList?> FindListAsync(Guid id, CancellationToken cancellationToken) =>
        await context.TaskLists.FirstOrDefaultAsync(list => list.Id == id, cancellationToken);

    public async Task<TaskList?> FindListEvenDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        await context.TaskLists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(list => list.Id == id, cancellationToken);

    public async Task<bool> NameIsTakenAsync(string name, Guid? itself, CancellationToken cancellationToken)
    {
        // Filtered coarsely in the database and decided here, so that what "the
        // same name" means is Domain's and not a collation's.
        var taken = await context.TaskLists
            .Where(list => list.Id != itself)
            .Select(list => list.Name)
            .ToListAsync(cancellationToken);

        return taken.Any(one => TaskTitle.Same(one, name));
    }

    public async Task<IReadOnlyList<PersonalTask>> TasksAsync(
        Guid list, CancellationToken cancellationToken) =>
        await context.Tasks
            .Where(task => task.ListId == list)
            .OrderBy(task => task.Position)
            .ThenBy(task => task.Id)
            .ToListAsync(cancellationToken);

    public async Task<PersonalTask?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Tasks.FirstOrDefaultAsync(task => task.Id == id, cancellationToken);

    public async Task<PersonalTask?> FindEvenDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Tasks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(task => task.Id == id, cancellationToken);

    public async Task AddAsync(TaskList list, CancellationToken cancellationToken)
    {
        await context.TaskLists.AddAsync(list, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(PersonalTask task, CancellationToken cancellationToken)
    {
        await context.Tasks.AddAsync(task, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(string what, CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, what, cancellationToken);

    public Task DeleteAsync(
        PersonalTask task, Caller by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        task.Delete(by, at);
        task.Touch(at);

        return GuardedSave.SaveAsync(context, "The task", cancellationToken);
    }

    public async Task DeleteAsync(
        TaskList list, Caller by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);

        // Everything in it goes with it, under one moment, so that the whole
        // thing is one Trash entry and comes back together.
        var going = await context.Tasks
            .Where(task => task.ListId == list.Id)
            .ToListAsync(cancellationToken);

        foreach (var task in going)
        {
            task.Delete(by, at);
            task.Touch(at);
        }

        list.Delete(by, at);
        list.Touch(at);

        await GuardedSave.SaveAsync(context, "The list", cancellationToken);
    }
}
