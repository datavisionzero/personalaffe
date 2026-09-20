using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

public sealed class BookmarkOpeningConfiguration : IEntityTypeConfiguration<BookmarkOpening>
{
    public void Configure(EntityTypeBuilder<BookmarkOpening> builder)
    {
        builder.ToTable("bookmark_openings");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.BookmarkId).HasColumnName("bookmark_id");
        builder.HasOne<Bookmark>().WithMany().HasForeignKey(row => row.BookmarkId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BookmarkOpenDayConfiguration : IEntityTypeConfiguration<BookmarkOpenDay>
{
    public void Configure(EntityTypeBuilder<BookmarkOpenDay> builder)
    {
        builder.ToTable("bookmark_open_days");
        builder.HasKey(row => new { row.BookmarkId, row.Day });
        builder.Property(row => row.BookmarkId).HasColumnName("bookmark_id");
        builder.Property(row => row.Day).HasColumnName("day").HasColumnType("date");
        builder.Property(row => row.Count).HasColumnName("count");
        builder.Property(row => row.LastOpenedAt).HasColumnName("last_opened_at");
        builder.HasIndex(row => row.Day);
        builder.HasOne<Bookmark>().WithMany().HasForeignKey(row => row.BookmarkId).OnDelete(DeleteBehavior.Cascade);
    }
}
