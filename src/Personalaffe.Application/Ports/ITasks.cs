using Personalaffe.Domain;
using Personalaffe.Domain.Tasks;

namespace Personalaffe.Application.Ports;

/// <summary>One list, and how much is in it.</summary>
/// <param name="Open">
/// How many of its tasks are not done. The first thing any client draws is
/// which list has anything in it, and a count is cheaper than every task.
/// </param>
public sealed record TheList(TaskList List, int Open, int All);

/// <summary>
/// Where task lists and their tasks are kept (<see cref="TaskList"/>,
/// <see cref="PersonalTask"/>).
/// </summary>
/// <remarks>
/// Nothing here takes a caller. Permission and the application switch are
/// settled by the acts, once, through <c>ReachingAnApplication</c>.
/// </remarks>
public interface ITasks
{
    /// <summary>The lists, by name, with how much is open in each.</summary>
    Task<IReadOnlyList<TheList>> ListsAsync(CancellationToken cancellationToken);

    /// <summary>One list, or nothing. Deleted ones are not found.</summary>
    Task<TaskList?> FindListAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The same, seeing what is in the Trash.</summary>
    Task<TaskList?> FindListEvenDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another list is already called this.</summary>
    Task<bool> NameIsTakenAsync(string name, Guid? itself, CancellationToken cancellationToken);

    /// <summary>What is in a list, in the owner's order.</summary>
    Task<IReadOnlyList<PersonalTask>> TasksAsync(Guid list, CancellationToken cancellationToken);

    /// <summary>One task, or nothing. Deleted ones are not found.</summary>
    Task<PersonalTask?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The same, seeing what is in the Trash.</summary>
    Task<PersonalTask?> FindEvenDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Puts a new list down.</summary>
    Task AddAsync(TaskList list, CancellationToken cancellationToken);

    /// <summary>Puts a new task down.</summary>
    Task AddAsync(PersonalTask task, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the change an act has already made, refusing as <c>stale</c> if a
    /// row moved underneath it.
    /// </summary>
    Task SaveAsync(string what, CancellationToken cancellationToken);

    /// <summary>Sets a task aside.</summary>
    Task DeleteAsync(PersonalTask task, Caller by, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a list aside with every task in it, under one moment, so that the
    /// whole thing comes back together.
    /// </summary>
    Task DeleteAsync(TaskList list, Caller by, DateTimeOffset at, CancellationToken cancellationToken);
}
