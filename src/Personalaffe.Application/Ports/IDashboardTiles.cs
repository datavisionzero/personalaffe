using Personalaffe.Domain.Dashboard;

namespace Personalaffe.Application.Ports;

/// <summary>
/// Which tiles the owner has on their home page (<see cref="TileState"/>).
/// </summary>
/// <remarks>
/// Five rows, seeded by the migration that creates the table, so
/// <see cref="ReadAsync(CancellationToken)"/> reads all of them and the act
/// picks what it wants out of the answer — the same shape as
/// <see cref="IApplicationSwitch"/>, because it answers the same kind of
/// question.
/// </remarks>
public interface IDashboardTiles
{
    /// <summary>All of them, in the order <see cref="DashboardTile"/> lists them.</summary>
    Task<IReadOnlyList<TileState>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>One of them, as the owner is about to show or hide it.</summary>
    Task<TileState> ReadAsync(DashboardTile tile, CancellationToken cancellationToken);

    /// <summary>
    /// Stores what <see cref="TileState.Show"/> has already done, refusing as
    /// <c>stale</c> if the row moved underneath it.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
