using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Weather;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// Where the owner wants the weather for: one row, and the schema is what says
/// so.
/// </summary>
/// <remarks>
/// <c>singleton</c> is both the key and a column the check constraint holds to
/// true, which is how the owner's own table keeps itself to one row
/// (<see cref="OwnerConfiguration"/>). The row is seeded nowhere, at the same
/// fixed moment the other seeded tables use, so a migration produces the same
/// schema on every machine it runs on.
/// </remarks>
public sealed class WeatherPlaceConfiguration : IEntityTypeConfiguration<WeatherPlace>
{
    public void Configure(EntityTypeBuilder<WeatherPlace> builder)
    {
        builder.ToTable(
            "weather_place",
            table => table.HasCheckConstraint("ck_weather_place_singleton", "singleton"));

        builder.HasKey(place => place.Singleton).HasName("pk_weather_place");
        builder.Property(place => place.Singleton).HasColumnName("singleton");

        builder.Property(place => place.Name)
            .HasColumnName("name")
            .HasMaxLength(WeatherPlace.NameMaxLength);

        builder.Property(place => place.Latitude).HasColumnName("latitude");
        builder.Property(place => place.Longitude).HasColumnName("longitude");

        builder.Property(place => place.Units)
            .HasColumnName("units")
            .HasConversion(new SnakeCaseEnumConverter<WeatherUnits>())
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(place => place.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(place => place.Version);
        builder.Ignore(place => place.Configured);

        builder.HasData(WeatherPlace.Nowhere(ApplicationStateConfiguration.Seeded));
    }
}
