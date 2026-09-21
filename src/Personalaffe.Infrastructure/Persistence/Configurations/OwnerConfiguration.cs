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

        // The second factor, where there is one. The confirmed secret and the
        // offer that is not yet one are separate columns, because an offer that
        // was already in force is how an owner locks themselves out with a
        // mistyped app.
        builder.Property(owner => owner.TotpSecret).HasColumnName("totp_secret");
        builder.Property(owner => owner.TotpEnrolledAt).HasColumnName("totp_enrolled_at");
        builder.Property(owner => owner.PendingTotpSecret).HasColumnName("pending_totp_secret");
        builder.Property(owner => owner.PendingTotpSecretAt).HasColumnName("pending_totp_secret_at");
        builder.Property(owner => owner.LastTotpStep).HasColumnName("last_totp_step");

        // What the operator did on the machine, where the owner can see it.
        builder.Property(owner => owner.RecoveredAt).HasColumnName("recovered_at");

        builder.Property(owner => owner.InactivityLockPinHash)
            .HasColumnName("inactivity_lock_pin_hash");
        builder.Property(owner => owner.InactivityLockMinutes)
            .HasColumnName("inactivity_lock_minutes")
            .HasDefaultValue(Owner.DefaultInactivityLockMinutes)
            .IsRequired();
        builder.Property(owner => owner.InactivityLockVersion)
            .HasColumnName("inactivity_lock_version")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Ignore(owner => owner.SecondFactorEnabled);
        builder.Ignore(owner => owner.InactivityLockEnabled);

        builder.Property(owner => owner.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(owner => owner.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
