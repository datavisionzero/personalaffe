using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The switch: one row per application, keyed by the application itself.
/// </summary>
/// <remarks>
/// <para>
/// The four rows are seeded here rather than written by the first caller to
/// look, so that the table is complete the moment the migration finishes and a
/// read is a read rather than a read that might have to insert.
/// </para>
/// <para>
/// <c>updated_at</c> is a concurrency token, as every guarded object's is
/// (<c>docs/api.md</c>, The guarded write). Two browsers switching the same
/// application at once is exactly the case the guard exists for, and a switch
/// is cheap to reissue and unpleasant to lose.
/// </para>
/// </remarks>
public sealed class ApplicationStateConfiguration : IEntityTypeConfiguration<ApplicationState>
{
    /// <summary>
    /// What the seeded rows carry as their first version. A fixed moment,
    /// because a migration that inserted <c>now()</c> would produce a different
    /// schema on every machine it ran on and EF compares the model to decide
    /// whether one is needed.
    /// </summary>
    public static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<ApplicationState> builder)
    {
        builder.ToTable("application");

        builder.HasKey(state => state.Application).HasName("pk_application");
        builder.Property(state => state.Application)
            .HasColumnName("application")
            .HasConversion(new SnakeCaseEnumConverter<WorkspaceApplication>())
            .HasMaxLength(32);

        builder.Property(state => state.Enabled).HasColumnName("enabled").IsRequired();

        builder.Property(state => state.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(state => state.Version);

        builder.HasData(Enum.GetValues<WorkspaceApplication>()
            .Select(application => ApplicationState.Fresh(application, Seeded)));
    }
}
