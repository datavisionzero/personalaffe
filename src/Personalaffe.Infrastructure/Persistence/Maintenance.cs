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
        await context.MaintenancePause.FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "The maintenance pause row is missing. The migration that creates the table creates "
            + "its one row; a database this is true of has been edited by hand.");

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The maintenance pause", cancellationToken);
}
