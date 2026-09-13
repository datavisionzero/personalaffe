using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The agents the owner has let in, one row each, with the permission for each
/// application in a column of its own.
/// </summary>
/// <remarks>
/// Four columns rather than a map in one: a check constraint that says
/// <c>knowledge in ('none', 'read', 'read_write')</c> can be read at a psql
/// prompt, and answering "what may this agent do" is then a row rather than a
/// document to parse. A fifth application would be a migration, which is the
/// right amount of friction for adding a place the owner's content lives.
/// </remarks>
public sealed class AgentAccessConfiguration : IEntityTypeConfiguration<AgentAccess>
{
    private const string Allowed = "in ('none', 'read', 'read_write')";

    public void Configure(EntityTypeBuilder<AgentAccess> builder)
    {
        builder.ToTable("agent_access", table =>
        {
            table.HasCheckConstraint("ck_agent_access_scratchpad", $"scratchpad {Allowed}");
            table.HasCheckConstraint("ck_agent_access_knowledge", $"knowledge {Allowed}");
            table.HasCheckConstraint("ck_agent_access_tasks", $"tasks {Allowed}");
            table.HasCheckConstraint("ck_agent_access_files", $"files {Allowed}");
        });

        builder.HasKey(access => access.Id).HasName("pk_agent_access");
        builder.Property(access => access.Id).HasColumnName("id");

        builder.Property(access => access.Name)
            .HasColumnName("name")
            .HasMaxLength(AgentAccess.NameMaxLength)
            .IsRequired();

        // The unique index on the name is case-insensitive, which the model
        // cannot say: it is `lower(name)` in the migration that created the
        // table, and it is named here so that nobody reading the model believes
        // it is missing. Two agents called the same thing are two things nobody
        // can tell apart at the moment of revoking one.

        builder.ComplexProperty(access => access.Permissions, permissions =>
        {
            permissions.Property(granted => granted.Scratchpad)
                .HasColumnName("scratchpad")
                .HasConversion(new SnakeCaseEnumConverter<Permission>())
                .IsRequired();
            permissions.Property(granted => granted.Knowledge)
                .HasColumnName("knowledge")
                .HasConversion(new SnakeCaseEnumConverter<Permission>())
                .IsRequired();
            permissions.Property(granted => granted.Tasks)
                .HasColumnName("tasks")
                .HasConversion(new SnakeCaseEnumConverter<Permission>())
                .IsRequired();
            permissions.Property(granted => granted.Files)
                .HasColumnName("files")
                .HasConversion(new SnakeCaseEnumConverter<Permission>())
                .IsRequired();
        });

        builder.Property(access => access.TokenPrefix)
            .HasColumnName("token_prefix")
            .HasMaxLength(TokenSecret.PrefixLength)
            .IsRequired();

        // What the door looks a presented token up by, and the index is the
        // index: nothing is compared as text, so a miss and a hit cost one
        // lookup each.
        builder.Property(access => access.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.HasIndex(access => access.TokenHash)
            .IsUnique()
            .HasDatabaseName("agent_access_token_hash");

        builder.Property(access => access.TokenIssuedAt).HasColumnName("token_issued_at").IsRequired();
        builder.Property(access => access.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(access => access.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(access => access.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(access => access.RevokedAt).HasColumnName("revoked_at");

        builder.Ignore(access => access.Revoked);
    }
}
