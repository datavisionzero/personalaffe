using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The shape <see cref="Search"/>'s four statements answer in.
/// </summary>
/// <remarks>
/// <para>
/// <c>HasNoKey</c> and <c>ToView(null)</c>: a row of an answer is not a thing
/// this instance stores. EF needs the type in its model to read a
/// <c>FromSql</c> result into it, and those two lines are what say that no
/// table stands behind it — so no migration creates one and nothing is tracked.
/// </para>
/// <para>
/// Every column is named here, because the statements are hand-written and the
/// context has no naming convention: what maps is what both sides spell the
/// same, and that is worth saying once rather than discovering at runtime.
/// </para>
/// </remarks>
internal sealed class FoundRowConfiguration : IEntityTypeConfiguration<FoundRow>
{
    public void Configure(EntityTypeBuilder<FoundRow> builder)
    {
        builder.HasNoKey().ToView(null);

        builder.Property(row => row.Id).HasColumnName("id");
        builder.Property(row => row.Title).HasColumnName("title");
        builder.Property(row => row.Snippet).HasColumnName("snippet");
        builder.Property(row => row.Within).HasColumnName("within");
        builder.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        builder.Property(row => row.Rank).HasColumnName("rank");
    }
}
