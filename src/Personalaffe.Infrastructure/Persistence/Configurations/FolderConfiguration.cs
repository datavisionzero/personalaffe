using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Files;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The folders of the Files application.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The tree is a self-reference and not a path column.</strong> A
/// stored path would have to be rewritten for every descendant of a folder
/// somebody renamed, which is the one operation this product promises is
/// cheap — and the first time the rewrite half-failed the tree would disagree
/// with itself. What is stored is which folder a folder is in, and a path is
/// worked out by walking, bounded by <see cref="Folder.MaxDepth"/>.
/// </para>
/// <para>
/// <strong>There is no foreign key on the parent.</strong> A recoverable row
/// points at a recoverable row, and both of them can be in the Trash while the
/// tree still has to answer questions about them; a cascade would be the
/// database deciding what a delete means, which is <see cref="Restoration"/>'s
/// decision and the module's to carry out.
/// </para>
/// <para>
/// No unique index on <c>(parent_id, name)</c> either, for the same reason: the
/// rule is that two <em>live</em> things in one folder cannot share a name, and
/// a deleted one is allowed to keep its name until somebody restores it. An
/// index that cannot say "live" would refuse a deletion the owner is entitled
/// to undo. The check is in the acts, where "live" is a word that means
/// something.
/// </para>
/// </remarks>
public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("folders");

        builder.HasKey(folder => folder.Id).HasName("pk_folders");
        builder.Property(folder => folder.Id).HasColumnName("id");

        builder.Property(folder => folder.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(FileName.MaxBytes);

        builder.Property(folder => folder.ParentId).HasColumnName("parent_id");

        builder.Property(folder => folder.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(folder => folder.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(folder => folder.Version);

        builder.IsRecoverable();

        // What every listing asks: what is in this folder. The root is the rows
        // whose parent is null, which this index answers as well.
        builder.HasIndex(folder => folder.ParentId).HasDatabaseName("ix_folders_parent_id");
    }
}
