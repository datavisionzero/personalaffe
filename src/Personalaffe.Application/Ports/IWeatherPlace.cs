using Personalaffe.Domain.Weather;

namespace Personalaffe.Application.Ports;

/// <summary>
/// Where the owner wants the weather for (<see cref="WeatherPlace"/>).
/// </summary>
/// <remarks>
/// One row, seeded by the migration that creates the table, so this never
/// inserts and a read never has to decide what a missing row would have meant
/// — the same arrangement as <see cref="IApplicationSwitch"/> and
/// <see cref="IDashboardTiles"/>.
/// </remarks>
public interface IWeatherPlace
{
    /// <summary>The place, which exists even when it is nowhere.</summary>
    Task<WeatherPlace> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores what <see cref="WeatherPlace.Set"/> has already done, refusing as
    /// <c>stale</c> if the row moved underneath it.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
