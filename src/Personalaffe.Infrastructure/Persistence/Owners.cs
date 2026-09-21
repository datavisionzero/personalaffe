using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>The owner, out of the one row there can be (<see cref="IOwners"/>).</summary>
public sealed class Owners(PersonalaffeDbContext context) : IOwners
{
    /// <summary>What Postgres calls a unique index that was violated.</summary>
    private const string UniqueViolation = "23505";

    public Task<bool> ExistsAsync(CancellationToken cancellationToken) =>
        context.Owners.AnyAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <strong>Single and not First.</strong> There is one owner and the
    /// database is what says so — `singleton` is always true, under a unique
    /// index, beside a check constraint. `First` would be asking for an
    /// arbitrary one of however many there are, which is a weaker statement
    /// than this product makes; EF also says so out loud, because a
    /// row-limiting operator with no <c>OrderBy</c> is a warning in the
    /// operator's log for a query that cannot be ambiguous.
    /// </remarks>
    public Task<Owner?> FindAsync(CancellationToken cancellationToken) =>
        context.Owners.SingleOrDefaultAsync(cancellationToken);

    public async Task<IOwnerChange> BeginChangeAsync(CancellationToken cancellationToken) =>
        new OwnerChange(await context.Database.BeginTransactionAsync(cancellationToken));

    public async Task<Owner?> FindForUpdateAsync(CancellationToken cancellationToken)
    {
        // Authentication read the owner before this act. Detach that snapshot:
        // after waiting for another attempt's row lock, the serialized decision
        // must use what that attempt committed rather than the tracked value
        // from before the wait.
        foreach (var entry in context.ChangeTracker.Entries<Owner>())
        {
            entry.State = EntityState.Detached;
        }

        return await context.Owners
            .FromSqlRaw("select * from owner for update")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Owner owner, CancellationToken cancellationToken)
    {
        context.Owners.Add(owner);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure)
            when (failure.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Two callers set up at once and this one lost. The answer is the
            // one a second setup gets, because that is what happened.
            context.Entry(owner).State = EntityState.Detached;
            throw SetUpTheInstance.AlreadySetUp();
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    private sealed class OwnerChange(IDbContextTransaction transaction) : IOwnerChange
    {
        public Task CompleteAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
