using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Appearance;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// What this instance is called and what its mark looks like: one row, and the
/// schema is what says so.
/// </summary>
/// <remarks>
/// <c>singleton</c> is both the key and a column the check constraint holds to
/// true, the way the owner's own table keeps itself to one row
/// (<see cref="OwnerConfiguration"/>). The colour and the shape are stored as
/// the closed words they are and never as CSS: what a colour looks like is the
/// web application's token layer's business, and a database holding
/// <c>oklch(…)</c> would be a second place to change a theme in.
/// </remarks>
public sealed class InstanceAppearanceConfiguration : IEntityTypeConfiguration<InstanceAppearance>
{
    public void Configure(EntityTypeBuilder<InstanceAppearance> builder)
    {
        builder.ToTable(
            "instance_appearance",
            table => table.HasCheckConstraint("ck_instance_appearance_singleton", "singleton"));

        builder.HasKey(appearance => appearance.Singleton).HasName("pk_instance_appearance");
        builder.Property(appearance => appearance.Singleton).HasColumnName("singleton");

        builder.Property(appearance => appearance.Title)
            .HasColumnName("title")
            .HasMaxLength(InstanceAppearance.TitleMaxLength);

        builder.Property(appearance => appearance.Colour)
            .HasColumnName("colour")
            .HasConversion(new SnakeCaseEnumConverter<MarkColour>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(appearance => appearance.Shape)
            .HasColumnName("shape")
            .HasConversion(new SnakeCaseEnumConverter<MarkShape>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(appearance => appearance.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(appearance => appearance.Version);
        builder.Ignore(appearance => appearance.Named);

        builder.HasData(InstanceAppearance.Unnamed(ApplicationStateConfiguration.Seeded));
    }
}
