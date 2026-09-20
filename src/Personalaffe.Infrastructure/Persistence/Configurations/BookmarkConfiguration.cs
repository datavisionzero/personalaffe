using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

public sealed class BookmarkConfiguration : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> builder)
    {
        builder.ToTable("bookmarks");
        builder.HasKey(row => row.Id).HasName("pk_bookmarks");
        builder.Property(row => row.Id).HasColumnName("id");
        builder.Property(row => row.Title).HasColumnName("title").HasMaxLength(BookmarkText.MaxTitleLength).IsRequired();
        builder.Property(row => row.Url).HasColumnName("url").HasMaxLength(BookmarkText.MaxUrlLength).IsRequired();
        builder.Property(row => row.Description).HasColumnName("description").IsRequired();
        builder.Property(row => row.FolderId).HasColumnName("folder_id");
        builder.Property(row => row.FavoritePosition).HasColumnName("favorite_position");
        builder.Property(row => row.PrivateOrigin).HasColumnName("private_origin");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired().IsConcurrencyToken();
        builder.Ignore(row => row.Version);
        builder.IsRecoverable();
        builder.HasIndex(row => row.FolderId);
    }
}
