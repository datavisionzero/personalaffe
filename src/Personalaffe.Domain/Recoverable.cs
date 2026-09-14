namespace Personalaffe.Domain;

/// <summary>
/// Lasting content, which is deleted by being set aside: it leaves ordinary
/// reads, keeps its row, and can be brought back until its retention runs out
/// (<c>CONTEXT.md</c>, Trash).
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is an interface with two members and not a base class.</strong>
/// Knowledge, Tasks and Files are three modules that each own their table, and
/// what they share is these two columns, the query filter over them and the
/// acts that read the Trash — not an inheritance chain, and not a row in a
/// fourth table standing for a row in one of theirs (<c>docs/codebase.md</c>).
/// </para>
/// <para>
/// A Scratchpad entry does not implement it. Scratchpad deletion is immediately
/// permanent (VISION.md), and the type system is where that split is said out
/// loud rather than in a comment somebody has to find.
/// </para>
/// </remarks>
public interface IRecoverable
{
    /// <summary>When it was deleted, or nothing while it is not.</summary>
    DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Who deleted it, kept as it read at the time.</summary>
    Actor? DeletedBy { get; set; }
}

/// <summary>The two acts every piece of lasting content has, and the questions about them.</summary>
public static class Recoverable
{
    /// <summary>Whether this is in the Trash.</summary>
    public static bool IsDeleted(this IRecoverable content) =>
        (content ?? throw new ArgumentNullException(nameof(content))).DeletedAt is not null;

    /// <summary>
    /// Sets it aside. Deleting what is already deleted changes nothing —
    /// including who deleted it, which is the first deletion's answer and not
    /// the second one's.
    /// </summary>
    public static void Delete(this IRecoverable content, Caller by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.IsDeleted())
        {
            return;
        }

        content.DeletedAt = at;
        content.DeletedBy = Actor.Of(by);
    }

    /// <summary>Brings it back. Restoring what is not deleted changes nothing.</summary>
    public static void Restore(this IRecoverable content)
    {
        ArgumentNullException.ThrowIfNull(content);

        content.DeletedAt = null;
        content.DeletedBy = null;
    }

    /// <summary>
    /// When the purge will remove this for good, or nothing while it is not
    /// deleted.
    /// </summary>
    public static DateTimeOffset? ExpiresAt(this IRecoverable content, TimeSpan retention) =>
        (content ?? throw new ArgumentNullException(nameof(content))).DeletedAt is { } deletedAt
            ? deletedAt + retention
            : null;

    /// <summary>
    /// The refusal an address answers with while what used to be there is in
    /// the Trash: a 404 that says it can still be brought back, and by when.
    /// </summary>
    public static Refusal Gone(this IRecoverable content, string what, TimeSpan retention)
    {
        ArgumentNullException.ThrowIfNull(content);

        var deletedAt = content.DeletedAt
            ?? throw new InvalidOperationException("Content that is not deleted has not gone anywhere.");

        return Refusal.Deleted(
            $"{what} was deleted by {content.DeletedBy?.Describe() ?? "somebody"} and is in the Trash. "
            + "It can be restored until it expires.",
            deletedAt,
            deletedAt + retention);
    }
}
