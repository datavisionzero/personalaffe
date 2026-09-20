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
        builder.Property(row => row.ReadLaterAt).HasColumnName("read_later_at");
        builder.HasIndex(row => row.ReadLaterAt);
        builder.Property(row => row.Tags).HasColumnName("tags").HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]").IsRequired();
        builder.HasIndex(row => row.Tags).HasMethod("gin");
        builder.Property(row => row.FolderId).HasColumnName("folder_id");
        builder.Property(row => row.FavoritePosition).HasColumnName("favorite_position");
        builder.Property(row => row.PrivateOrigin).HasColumnName("private_origin");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired().IsConcurrencyToken();
        builder.Ignore(row => row.Version);
        builder.IsRecoverable();
        builder.IsSearchable("bookmarks", $"{SearchIndex.Called("title")} || {SearchIndex.Says("description")} || "
            + "setweight(to_tsvector('simple', regexp_replace(coalesce(url, ''), '[^[:alnum:]]+', ' ', 'g')), 'B')");
        builder.HasIndex(row => row.FolderId);
    }
}
