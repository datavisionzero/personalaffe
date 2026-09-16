using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// Whether this instance is being held still while a backup takes both of its
/// stores: one row, and the schema is what says so.
/// </summary>
/// <remarks>
/// <c>singleton</c> is both the key and a column the check constraint holds to
/// true, the same way the owner's table and the weather place keep themselves
/// to one row. It is seeded by the migration at the same fixed moment the other
/// seeded tables use, so that reading it is a read rather than a read that
/// might have to insert — and so that it has a version to be written against
/// from the first start.
/// </remarks>
public sealed class MaintenancePauseConfiguration : IEntityTypeConfiguration<MaintenancePause>
{
    public void Configure(EntityTypeBuilder<MaintenancePause> builder)
    {
        builder.ToTable(
            "maintenance_pause",
            table => table.HasCheckConstraint("ck_maintenance_pause_singleton", "singleton"));

        builder.HasKey(pause => pause.Singleton).HasName("pk_maintenance_pause");
        builder.Property(pause => pause.Singleton).HasColumnName("singleton");

        builder.Property(pause => pause.Since).HasColumnName("since");
        builder.Property(pause => pause.Until).HasColumnName("until");

        builder.Property(pause => pause.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasData(MaintenancePause.Nothing(ApplicationStateConfiguration.Seeded));
    }
}
