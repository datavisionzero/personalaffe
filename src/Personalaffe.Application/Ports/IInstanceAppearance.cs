using Personalaffe.Domain.Appearance;

namespace Personalaffe.Application.Ports;

/// <summary>
/// What this instance is called and what its mark looks like
/// (<see cref="InstanceAppearance"/>).
/// </summary>
/// <remarks>
/// One row, seeded by the migration that creates the table, so this never
/// inserts and a read never has to decide what a missing row would have meant
/// — the same arrangement as <see cref="IWeatherPlace"/> and
/// <see cref="IApplicationSwitch"/>.
/// </remarks>
public interface IInstanceAppearance
{
    /// <summary>The appearance, which exists even while nobody has set it.</summary>
    Task<InstanceAppearance> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores what <see cref="InstanceAppearance.Set"/> has already done,
    /// refusing as <c>stale</c> if the row moved underneath it.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
