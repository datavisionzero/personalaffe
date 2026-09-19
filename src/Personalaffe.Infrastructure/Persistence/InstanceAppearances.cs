using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Appearance;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// What this instance is called and what its mark looks like
/// (<see cref="IInstanceAppearance"/>).
/// </summary>
/// <remarks>
/// The row is created by the migration that creates the table, so nothing here
/// inserts: the appearance exists from the first start and is the product's own
/// until the owner says otherwise.
/// </remarks>
public sealed class InstanceAppearances(PersonalaffeDbContext context) : IInstanceAppearance
{
    public async Task<InstanceAppearance> ReadAsync(CancellationToken cancellationToken) =>
        // Single and not First, for the reason WeatherPlaces gives: the table
        // holds at most one row and the database is what says so.
        await context.InstanceAppearance.SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "The instance appearance is missing. The migration that creates the table creates its "
            + "one row; a database this is true of has been edited by hand.");

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The appearance", cancellationToken);
}
