using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Scratchpad;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The Scratchpad's one table.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It does not call <c>IsRecoverable</c>, and there is no
/// <c>deleted_at</c> here.</strong> Every other module's table carries the
/// deletion columns and the filter that keeps set-aside rows out of every read;
/// this one destroys what it deletes (<c>docs/api.md</c>, Deleting sets content
/// aside), so a column for a state it cannot be in would be a column that
/// invites somebody to wire it into the Trash by habit.
/// </para>
/// <para>
/// <c>updated_at</c> is a concurrency token, as every guarded object's is
/// (<c>docs/api.md</c>, The guarded write). A phone and a desk editing the same
/// entry is the ordinary case in a workspace one person carries around.
/// </para>
/// <para>
/// Two indexes, one per question the module actually asks: the list reads by
/// <c>created_at</c> descending, and the sweep asks for the unpinned rows whose
/// <c>updated_at</c> is past a deadline.
/// </para>
/// </remarks>
public sealed class ScratchpadEntryConfiguration : IEntityTypeConfiguration<ScratchpadEntry>
{
    public void Configure(EntityTypeBuilder<ScratchpadEntry> builder)
    {
        builder.ToTable("scratchpad_entries");

        builder.HasKey(entry => entry.Id).HasName("pk_scratchpad_entries");
        builder.Property(entry => entry.Id).HasColumnName("id");

        builder.Property(entry => entry.Text)
            .HasColumnName("text")
            .IsRequired()
            // No length in the column. The limit is in bytes of UTF-8 and the
            // domain holds it (ScratchpadEntry.MaxBytes); a character count here
            // would be a second limit that means something different, and the
            // refusal the owner should get is the one that names the number.
            .HasColumnType("text");

        builder.Property(entry => entry.Pinned).HasColumnName("pinned").IsRequired();

        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(entry => entry.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(entry => entry.Version);

        builder.HasIndex(entry => entry.CreatedAt).HasDatabaseName("ix_scratchpad_entries_created_at");

        builder.HasIndex(entry => new { entry.Pinned, entry.UpdatedAt })
            .HasDatabaseName("ix_scratchpad_entries_pinned_updated_at");

        // An entry has no title, so it has no `A` half: there is nothing it
        // is called, only what it says (SearchIndex). That is also why a
        // found entry is drawn by its first line — the store makes one.
        builder.IsSearchable("scratchpad_entries", SearchIndex.Says("text"));
    }
}
