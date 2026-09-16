using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Dashboard;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The home page's tiles: one row per tile, keyed by the tile itself.
/// </summary>
/// <remarks>
/// The rows are seeded here rather than written by the first caller to look, so
/// that the table is complete the moment the migration finishes — the same
/// arrangement as <see cref="ApplicationStateConfiguration"/>, down to the
/// fixed seed moment, which a migration needs so that the schema it produces is
/// the same on every machine it runs on.
/// </remarks>
public sealed class DashboardTileConfiguration : IEntityTypeConfiguration<TileState>
{
    public void Configure(EntityTypeBuilder<TileState> builder)
    {
        builder.ToTable("dashboard_tile");

        builder.HasKey(state => state.Tile).HasName("pk_dashboard_tile");
        builder.Property(state => state.Tile)
            .HasColumnName("tile")
            .HasConversion(new SnakeCaseEnumConverter<DashboardTile>())
            .HasMaxLength(32);

        builder.Property(state => state.Shown).HasColumnName("shown").IsRequired();

        builder.Property(state => state.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(state => state.Version);

        builder.HasData(Enum.GetValues<DashboardTile>()
            .Select(tile => TileState.Fresh(tile, ApplicationStateConfiguration.Seeded)));
    }
}
