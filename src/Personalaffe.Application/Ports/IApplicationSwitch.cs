using Personalaffe.Domain;

namespace Personalaffe.Application.Ports;

/// <summary>
/// Which applications this workspace has switched on
/// (<see cref="ApplicationState"/>).
/// </summary>
/// <remarks>
/// Four rows, so <see cref="ReadAsync"/> reads all of them and the acts pick
/// the one they want out of the answer. A port that took one application per
/// call would be four round trips for the screen that draws the switches, and
/// one query is cheaper than the cache that avoids it.
/// </remarks>
public interface IApplicationSwitch
{
    /// <summary>All four, in the order <see cref="WorkspaceApplication"/> lists them.</summary>
    Task<IReadOnlyList<ApplicationState>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>One of them, as a caller about to be let into it is checked against.</summary>
    Task<ApplicationState> ReadAsync(WorkspaceApplication application, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the switch that <see cref="ApplicationState.Switch"/> has already
    /// made, refusing as <c>stale</c> if the row moved underneath it.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
