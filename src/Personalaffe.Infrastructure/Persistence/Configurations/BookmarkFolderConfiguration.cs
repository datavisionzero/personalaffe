using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

public sealed class BookmarkFolderConfiguration : IEntityTypeConfiguration<BookmarkFolder>
{
    public void Configure(EntityTypeBuilder<BookmarkFolder> builder)
    {
        builder.ToTable("bookmark_folders");
        builder.HasKey(row => row.Id).HasName("pk_bookmark_folders");
        builder.Property(row => row.Id).HasColumnName("id");
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(BookmarkText.MaxTitleLength).IsRequired();
        builder.Property(row => row.ParentId).HasColumnName("parent_id");
        builder.Property(row => row.Private).HasColumnName("private");
        builder.Property(row => row.PrivateOrigin).HasColumnName("private_origin");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired().IsConcurrencyToken();
        builder.Ignore(row => row.Version);
        builder.IsRecoverable();
        builder.HasIndex(row => row.ParentId);
    }
}
