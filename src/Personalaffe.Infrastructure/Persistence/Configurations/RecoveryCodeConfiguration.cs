using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The codes the owner keeps on paper. Digests, like every other secret this
/// instance stores.
/// </summary>
public sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("recovery_code");

        builder.HasKey(code => code.Id).HasName("pk_recovery_code");
        builder.Property(code => code.Id).HasColumnName("id");

        builder.Property(code => code.OwnerId).HasColumnName("owner_id").IsRequired();

        builder.HasOne<Owner>()
            .WithMany()
            .HasForeignKey(code => code.OwnerId)
            .HasConstraintName("fk_recovery_code_owner")
            .OnDelete(DeleteBehavior.Cascade);

        // What a presented code is looked up by, within the owner's own set.
        builder.Property(code => code.CodeHash).HasColumnName("code_hash").IsRequired();
        builder.HasIndex(code => new { code.OwnerId, code.CodeHash })
            .IsUnique()
            .HasDatabaseName("recovery_code_hash");

        builder.Property(code => code.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(code => code.UsedAt).HasColumnName("used_at");

        builder.Ignore(code => code.Used);
    }
}
