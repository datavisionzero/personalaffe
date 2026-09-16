using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Files;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The files of the Files application — their metadata. The bytes are on the
/// volume (<c>Infrastructure/Files/LocalFileBytes.cs</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>No column holds the address of the bytes.</strong> It is
/// <c>StorageAddress.Of(id)</c> and is derived wherever it is wanted, because a
/// stored path is a second thing that has to agree with the first — and the
/// first is a primary key that cannot change.
/// </para>
/// <para>
/// <c>updated_at</c> is a concurrency token, as every guarded object's is, and
/// <c>IsRecoverable</c> brings the deletion columns, the filter that keeps
/// set-aside rows out of every read, and the index the sweep uses.
/// </para>
/// </remarks>
public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("files");

        builder.HasKey(file => file.Id).HasName("pk_files");
        builder.Property(file => file.Id).HasColumnName("id");

        builder.Property(file => file.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(FileName.MaxBytes);

        builder.Property(file => file.FolderId).HasColumnName("folder_id");

        builder.Property(file => file.Size).HasColumnName("size").IsRequired();

        builder.Property(file => file.MediaType)
            .HasColumnName("media_type")
            .IsRequired()
            .HasMaxLength(StoredFile.MediaTypeMaxLength);

        builder.Property(file => file.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(file => file.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(file => file.Address);
        builder.Ignore(file => file.Version);

        builder.IsRecoverable();

        builder.HasIndex(file => file.FolderId).HasDatabaseName("ix_files_folder_id");

        // The name and nothing else. VISION.md draws the line here: what is
        // in a file is never indexed, which is what keeps one search over a
        // workspace from becoming a document search over a disk.
        builder.IsSearchable("files", SearchIndex.Called("name"));
    }
}
