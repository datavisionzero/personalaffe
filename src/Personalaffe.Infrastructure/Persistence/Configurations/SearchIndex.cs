using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The column one search looks through, on the table of whichever application
/// owns it (<c>Personalaffe.Domain.Search.Needle</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is a stored generated column, so there is no index to keep up to
/// date.</strong> Postgres computes it from the row on every insert and update,
/// which means no trigger, no second write from the application, and no way for
/// the index to disagree with the content — including for a row an agent wrote
/// over the API while the owner's browser was writing another, and including
/// for whatever a restore puts back. A module that stores content stores it
/// once and is searchable.
/// </para>
/// <para>
/// <strong>The configuration is <c>simple</c> and not <c>english</c>.</strong>
/// A dictionary stems and drops stop words in one language, and this workspace
/// is one person's: German notes beside English ones beside a file called
/// <c>Steuer-2026.pdf</c>. <c>english</c> would stem the English and leave the
/// German as noise, and would throw away <c>the</c>, <c>a</c> and <c>is</c>
/// from titles that are about those words. <c>simple</c> lowercases and does
/// nothing else, which is the behaviour a person searching their own writing
/// expects, and it is what makes every word a prefix worth matching.
/// </para>
/// <para>
/// <strong>What is weighted <c>A</c> is what the thing is called.</strong> A
/// title, a task's title, a file's name. The body is <c>B</c>. That is the
/// whole of the ranking: <c>ts_rank</c> reads the weights, so a page called
/// "Architecture" outranks a page that mentions architecture once, and nothing
/// downstream has to reorder anything.
/// </para>
/// </remarks>
public static class SearchIndex
{
    /// <summary>
    /// The shadow property the vector lives in. Shadow, because
    /// <c>Personalaffe.Domain</c> depends on nothing at all and
    /// <see cref="NpgsqlTsVector"/> is a Postgres type: the column exists in
    /// this layer and the entity never sees it.
    /// </summary>
    public const string Vector = "SearchVector";

    /// <summary>The name the column has in every table that carries one.</summary>
    public const string Column = "search_vector";

    /// <summary>
    /// What a title is worth against a body, as the two weights
    /// <c>ts_rank</c> reads.
    /// </summary>
    public const char Name = 'A';

    /// <summary>What a body is worth.</summary>
    public const char Body = 'B';

    /// <summary>
    /// The <c>A</c> half of a vector: what the thing is called, stripped of
    /// everything that is not a letter or a digit so that
    /// <c>Budget-2026.final.pdf</c> is four words and not one token no prefix
    /// will ever reach. A needle is taken apart the same way, which is what
    /// makes the two meet.
    /// </summary>
    public static string Called(string column) =>
        $"setweight(to_tsvector('simple', regexp_replace(coalesce({column}, ''), '[^[:alnum:]]+', ' ', 'g')), '{Name}')";

    /// <summary>The <c>B</c> half: the prose, tokenised as prose.</summary>
    public static string Says(string column) =>
        $"setweight(to_tsvector('simple', coalesce({column}, '')), '{Body}')";

    /// <summary>
    /// Gives this table the column and the index over it.
    /// <paramref name="expression"/> is built from <see cref="Called"/> and
    /// <see cref="Says"/>, joined with <c>||</c>.
    /// </summary>
    public static EntityTypeBuilder<TContent> IsSearchable<TContent>(
        this EntityTypeBuilder<TContent> builder,
        string table,
        string expression)
        where TContent : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<NpgsqlTsVector>(Vector)
            .HasColumnName(Column)
            .HasComputedColumnSql(expression, stored: true);

        // GIN and not GiST: this index is read on every keystroke of a field
        // that answers while somebody types, and written when one person edits
        // one thing. That is the trade GIN is the right side of.
        builder.HasIndex(Vector)
            .HasMethod("gin")
            .HasDatabaseName($"ix_{table}_search");

        return builder;
    }
}
