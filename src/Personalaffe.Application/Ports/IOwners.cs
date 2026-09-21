using Personalaffe.Domain;

namespace Personalaffe.Application.Ports;

/// <summary>
/// The one owner, as the acts need to ask about them.
/// </summary>
/// <remarks>
/// There is no list and no lookup by address: an instance has one owner
/// (<c>CONTEXT.md</c>), so "find the owner" is the whole of reading, and
/// sign-in compares the address it was given against the one row rather than
/// searching for it.
/// </remarks>
public interface IOwners
{
    /// <summary>Whether this instance has been set up at all.</summary>
    Task<bool> ExistsAsync(CancellationToken cancellationToken);

    /// <summary>The owner, or nothing on an instance nobody has claimed.</summary>
    Task<Owner?> FindAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts a serialized owner change. Used where attempts from several
    /// browser sessions must share one persistent budget.
    /// </summary>
    Task<IOwnerChange> BeginChangeAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>The owner under the row lock opened by <see cref="BeginChangeAsync"/>.</summary>
    Task<Owner?> FindForUpdateAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>
    /// Writes the first and only owner, and refuses to write a second even
    /// when two callers ask at once: the schema decides that, not a read
    /// taken a moment earlier.
    /// </summary>
    /// <exception cref="Refusal">This instance already has an owner.</exception>
    Task AddAsync(Owner owner, CancellationToken cancellationToken);

    /// <summary>Persists changes to the owner that was read here.</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}

public interface IOwnerChange : IAsyncDisposable
{
    Task CompleteAsync(CancellationToken cancellationToken);
}
