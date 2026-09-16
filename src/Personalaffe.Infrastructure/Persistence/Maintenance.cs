using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// Whether this instance is being held still (<see cref="IMaintenance"/>).
/// </summary>
/// <remarks>
/// The row is created by the migration that creates the table, so nothing here
/// inserts: an instance is not being held still from its first start, and the
/// row exists to say so.
/// </remarks>
public sealed class Maintenance(PersonalaffeDbContext context) : IMaintenance
{
    public async Task<MaintenancePause> ReadAsync(CancellationToken cancellationToken) =>
        // Single and not First: the table holds at most one row and the
        // database is what says so — `singleton` is always true, under a key or
        // a unique index, beside a check constraint. `First` would be asking
        // for an arbitrary one of however many there are, which is both a
        // weaker statement than this code means and something EF says out loud:
        // a row-limiting operator with no `OrderBy` is a warning in the
        // operator's log, once per process, for a query that cannot be
        // ambiguous.
        await context.MaintenancePause.SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "The maintenance pause row is missing. The migration that creates the table creates "
            + "its one row; a database this is true of has been edited by hand.");

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The maintenance pause", cancellationToken);
}
