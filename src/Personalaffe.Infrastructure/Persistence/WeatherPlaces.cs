using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Weather;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// Where the owner wants the weather for (<see cref="IWeatherPlace"/>).
/// </summary>
/// <remarks>
/// The row is created by the migration that creates the table, so nothing here
/// inserts: the place exists from the first start and is nowhere until the
/// owner says otherwise.
/// </remarks>
public sealed class WeatherPlaces(PersonalaffeDbContext context) : IWeatherPlace
{
    public async Task<WeatherPlace> ReadAsync(CancellationToken cancellationToken) =>
        // Single and not First: the table holds at most one row and the
        // database is what says so — `singleton` is always true, under a key or
        // a unique index, beside a check constraint. `First` would be asking
        // for an arbitrary one of however many there are, which is both a
        // weaker statement than this code means and something EF says out loud:
        // a row-limiting operator with no `OrderBy` is a warning in the
        // operator's log, once per process, for a query that cannot be
        // ambiguous.
        await context.WeatherPlace.SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "The weather place is missing. The migration that creates the table creates its one "
            + "row; a database this is true of has been edited by hand.");

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The weather place", cancellationToken);
}
