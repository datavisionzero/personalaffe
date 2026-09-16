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
        await context.WeatherPlace.FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "The weather place is missing. The migration that creates the table creates its one "
            + "row; a database this is true of has been edited by hand.");

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The weather place", cancellationToken);
}
