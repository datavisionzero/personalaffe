using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The signed-in browsers. Rows here are secrets' shadows and nothing else: a
/// digest, two timestamps and whatever the browser called itself.
/// </summary>
public sealed class BrowserSessionConfiguration : IEntityTypeConfiguration<BrowserSession>
{
    public void Configure(EntityTypeBuilder<BrowserSession> builder)
    {
        builder.ToTable("browser_session");

        builder.HasKey(session => session.Id).HasName("pk_browser_session");
        builder.Property(session => session.Id).HasColumnName("id");

        builder.Property(session => session.OwnerId).HasColumnName("owner_id").IsRequired();

        builder.HasOne<Owner>()
            .WithMany()
            .HasForeignKey(session => session.OwnerId)
            .HasConstraintName("fk_browser_session_owner")
            .OnDelete(DeleteBehavior.Cascade);

        // What the door looks a presented cookie up by, and the index is the
        // index: the secret is never compared as text, so a miss and a hit cost
        // the same one lookup.
        builder.Property(session => session.SecretHash).HasColumnName("secret_hash").IsRequired();
        builder.HasIndex(session => session.SecretHash)
            .IsUnique()
            .HasDatabaseName("browser_session_secret_hash");

        builder.Property(session => session.Description)
            .HasColumnName("description")
            .HasMaxLength(BrowserSession.DescriptionMaxLength);

        builder.Property(session => session.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(session => session.LastUsedAt).HasColumnName("last_used_at").IsRequired();
        builder.Property(session => session.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(session => session.RevokedAt).HasColumnName("revoked_at");

        builder.Ignore(session => session.Revoked);
    }
}
