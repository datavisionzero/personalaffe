using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The knowledge pages.
/// </summary>
/// <remarks>
/// <para>
/// The tree is a self-reference and not a stored path, for the reason
/// <see cref="FolderConfiguration"/> gives: a path would have to be rewritten
/// for every descendant of a page somebody renamed, and renaming is the
/// operation this application promises is free.
/// </para>
/// <para>
/// No unique index on <c>(parent_id, title)</c> either. The rule is that two
/// <em>live</em> pages under one parent cannot share a title, and a deleted one
/// keeps its title until somebody restores it; an index that cannot say "live"
/// would refuse a deletion the owner is entitled to undo.
/// </para>
/// </remarks>
public sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable("pages");

        builder.HasKey(page => page.Id).HasName("pk_pages");
        builder.Property(page => page.Id).HasColumnName("id");

        builder.Property(page => page.Title)
            .HasColumnName("title")
            .IsRequired()
            .HasMaxLength(PageTitle.MaxLength);

        builder.Property(page => page.ParentId).HasColumnName("parent_id");

        builder.Property(page => page.Markdown)
            .HasColumnName("markdown")
            .IsRequired()
            // No length in the column: the limit is bytes of UTF-8 and the
            // domain holds it (Page.MaxBytes). A character count here would be a
            // second limit meaning something different, and the refusal the
            // owner should get is the one that names the number.
            .HasColumnType("text");

        builder.Property(page => page.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(page => page.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(page => page.Version);

        builder.IsRecoverable();

        builder.HasIndex(page => page.ParentId).HasDatabaseName("ix_pages_parent_id");
    }
}
