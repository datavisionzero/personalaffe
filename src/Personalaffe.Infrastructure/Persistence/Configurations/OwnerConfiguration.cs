using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The owner's table: one row, and the schema is what says so.
/// </summary>
/// <remarks>
/// <c>singleton</c> is a column that is always true with a unique index on it,
/// which makes a second row impossible however it is attempted — through an act
/// that skipped its check, through two callers setting up in the same
/// millisecond, or through somebody at a <c>psql</c> prompt. The check
/// constraint is what stops a row from opting out of the rule by being false.
/// </remarks>
public sealed class OwnerConfiguration : IEntityTypeConfiguration<Owner>
{
    public void Configure(EntityTypeBuilder<Owner> builder)
    {
        builder.ToTable("owner", table => table.HasCheckConstraint("ck_owner_singleton", "singleton"));

        builder.HasKey(owner => owner.Id).HasName("pk_owner");
        builder.Property(owner => owner.Id).HasColumnName("id");

        builder.Property(owner => owner.Email)
            .HasColumnName("email")
            .HasMaxLength(Owner.EmailMaxLength)
            .IsRequired();

        // What sign-in compares against. Not unique: uniqueness of an address
        // would be a rule about a second owner, and there is no second owner.
        builder.Property(owner => owner.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(Owner.EmailMaxLength)
            .IsRequired();

        builder.Property(owner => owner.PasswordHash).HasColumnName("password_hash").IsRequired();

        builder.Property(owner => owner.Singleton).HasColumnName("singleton").IsRequired();
        builder.HasIndex(owner => owner.Singleton).IsUnique().HasDatabaseName("owner_singleton");

        builder.Property(owner => owner.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(owner => owner.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
