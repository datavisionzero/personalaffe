namespace Personalaffe.Domain;

/// <summary>
/// A previous version of a piece of content, kept so that it can be recovered
/// (<c>CONTEXT.md</c>, Page revision).
/// </summary>
/// <remarks>
/// The content itself is not here, because it is a different shape in every
/// application that keeps history — a page's Markdown is not a task's title.
/// What is here is what every revision has: when the version it holds stopped
/// being the current one, and who made the change that ended it.
/// </remarks>
public interface IRevision
{
    /// <summary>When the version this holds stopped being the current one.</summary>
    DateTimeOffset At { get; }

    /// <summary>Who made the change that left this behind.</summary>
    Actor By { get; }
}

/// <summary>
/// How history behaves, for the applications that keep any — Knowledge
/// (PERSONAL-E7) to begin with.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is a convention and not a shared table.</strong> A module that
/// keeps history writes its own rows, with its own content in them, next to its
/// own table. Nothing here is a row standing for a row somewhere else, for the
/// same reason the Trash is not (<c>docs/codebase.md</c>).
/// </para>
/// <para>
/// <strong>Recovering writes forward.</strong> Putting an old version back is a
/// change like any other: it leaves a revision of what it replaced, so the thing
/// that was current a moment ago is itself recoverable. History only grows, and
/// the recovery is in it. Rewinding — dropping everything after the revision
/// being recovered — would make "undo" the one operation in this product that
/// destroys work.
/// </para>
/// <para>
/// <strong>Recovering is a guarded write on the object</strong>, not on the
/// revision (<c>docs/api.md</c>, The guarded write). A revision never changes,
/// so it has no version worth holding; what the caller has to be holding is the
/// object's, or recovering something read ten minutes ago would discard an edit
/// made five minutes ago.
/// </para>
/// <para>
/// <strong>Revisions belong to the thing they are of.</strong> Deleting it takes
/// them into the Trash with it, restoring brings them back, and removing it for
/// good removes them. A revision that outlived its page would be content the
/// owner believes they deleted.
/// </para>
/// </remarks>
public static class Revisions
{
    /// <summary>
    /// How many previous versions of one thing are kept.
    /// </summary>
    /// <remarks>
    /// A count and not an age. A page edited twice a year deserves its history
    /// as much as one edited twice a day, and "ninety days" would quietly throw
    /// away the whole history of everything the owner works on slowly — which is
    /// most of what a personal knowledge base is for. Fifty is enough that
    /// nobody reaches it by working and small enough that nothing grows without
    /// bound.
    /// </remarks>
    public const int Kept = 50;

    /// <summary>
    /// The revisions of one thing that fall outside what is kept, newest first
    /// — what the module deletes after writing a new one.
    /// </summary>
    /// <remarks>
    /// The current content is never among them: it lives on the object and not
    /// in a revision, which is the property that makes dropping the oldest safe
    /// to do without looking at anything else.
    /// </remarks>
    public static IReadOnlyList<TRevision> Superseded<TRevision>(IEnumerable<TRevision> revisions)
        where TRevision : IRevision =>
        [.. (revisions ?? throw new ArgumentNullException(nameof(revisions)))
            .OrderByDescending(revision => revision.At)
            .Skip(Kept)];
}
