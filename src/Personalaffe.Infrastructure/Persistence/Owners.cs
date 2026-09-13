using Microsoft.EntityFrameworkCore;
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

    public Task<Owner?> FindAsync(CancellationToken cancellationToken) =>
        context.Owners.FirstOrDefaultAsync(cancellationToken);

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
}
