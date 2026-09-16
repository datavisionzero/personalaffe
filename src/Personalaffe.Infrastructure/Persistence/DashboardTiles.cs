using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Dashboard;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// Which tiles are on the home page (<see cref="IDashboardTiles"/>).
/// </summary>
/// <remarks>
/// The rows are created by the migration that creates the table, so nothing
/// here inserts and nothing has to decide what a missing row means — the same
/// arrangement, and the same complaint about a hand-edited database, as
/// <see cref="ApplicationSwitch"/>.
/// </remarks>
public sealed class DashboardTiles(PersonalaffeDbContext context) : IDashboardTiles
{
    public async Task<IReadOnlyList<TileState>> ReadAsync(CancellationToken cancellationToken) =>
        [.. (await context.DashboardTiles.ToListAsync(cancellationToken)).OrderBy(state => state.Tile)];

    public async Task<TileState> ReadAsync(DashboardTile tile, CancellationToken cancellationToken) =>
        await context.DashboardTiles.FindAsync([tile], cancellationToken)
        ?? throw new InvalidOperationException(
            $"The row for the {tile} tile is missing. The migration that creates the table creates "
            + "every tile; a database this is true of has been edited by hand.");

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The tile", cancellationToken);
}
