using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// What the pages used to say — the first table in this product to use
/// <see cref="ContentRevisions.IsARevision"/>, which PERSONAL-E3 wrote for it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no query filter here and no <c>deleted_at</c>.</strong> A
/// revision is not deleted on its own: it belongs to its page and goes wherever
/// the page goes — into the Trash with it, back out of it, and for good with it
/// (<c>Domain/Revisions.cs</c>). The module does that in one statement rather
/// than by giving a revision a state of its own to get wrong.
/// </para>
/// <para>
/// No foreign key to the page, for the same reason the two trees have none on
/// their parents: what a delete means belongs to the module and to
/// <see cref="Restoration"/>, and a cascade would be the database deciding it.
/// </para>
/// </remarks>
public sealed class PageRevisionConfiguration : IEntityTypeConfiguration<PageRevision>
{
    public void Configure(EntityTypeBuilder<PageRevision> builder)
    {
        builder.ToTable("page_revisions");

        builder.HasKey(revision => revision.Id).HasName("pk_page_revisions");
        builder.Property(revision => revision.Id).HasColumnName("id");

        builder.Property(revision => revision.PageId).HasColumnName("page_id").IsRequired();

        builder.Property(revision => revision.Title)
            .HasColumnName("title")
            .IsRequired()
            .HasMaxLength(PageTitle.MaxLength);

        builder.Property(revision => revision.Markdown)
            .HasColumnName("markdown")
            .IsRequired()
            .HasColumnType("text");

        builder.IsARevision();

        // What a history reads and what the purge sweeps: everything of one
        // page, newest first.
        builder.HasIndex(revision => new { revision.PageId, revision.At })
            .HasDatabaseName("ix_page_revisions_page_id_at");
    }
}
