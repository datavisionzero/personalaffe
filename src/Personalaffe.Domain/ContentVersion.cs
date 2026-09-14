namespace Personalaffe.Domain;

/// <summary>
/// Which version of a piece of content a caller is holding: the moment it was
/// last changed, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// It exists so that a write can say what it is replacing. Two changes made
/// from the same starting point are a fact about this workspace and not an
/// accident — the owner edits a page in a browser while an agent edits it over
/// the API — and the product's answer is that the second one is refused rather
/// than that the first one quietly disappears.
/// </para>
/// <para>
/// The value is the object's <c>updated_at</c> because that is what every
/// client already reads and every table already carries; a version column
/// beside it would be a second thing that has to agree with the first. It is
/// truncated to microseconds on the way in, which is the precision Postgres
/// stores and the precision <c>docs/api.md</c> spells: a value a client reads
/// has to be a value it can send back and have recognised.
/// </para>
/// </remarks>
public sealed record ContentVersion
{
    private ContentVersion(DateTimeOffset updatedAt) => UpdatedAt = updatedAt;

    /// <summary>The version an object at <paramref name="updatedAt"/> is at.</summary>
    public static ContentVersion Of(DateTimeOffset updatedAt) => new(Truncated(updatedAt));

    /// <summary>When the object this version belongs to was last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Whether a caller holding <paramref name="held"/> is holding this one.
    /// </summary>
    public bool Matches(ContentVersion held) => UpdatedAt == (held?.UpdatedAt);

    /// <summary>
    /// The same moment with everything below a microsecond dropped.
    /// </summary>
    /// <remarks>
    /// .NET counts in hundreds of nanoseconds and Postgres in microseconds, so
    /// a timestamp that has been through the database is not bit for bit the
    /// one that went in. Comparing the two without this is a guard that refuses
    /// every write the moment somebody stores a value straight from
    /// <c>TimeProvider</c>.
    /// </remarks>
    private static DateTimeOffset Truncated(DateTimeOffset moment)
    {
        var utc = moment.ToUniversalTime();

        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);
    }
}
