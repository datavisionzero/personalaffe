namespace Personalaffe.Domain.Knowledge;

/// <summary>
/// What a knowledge page said before somebody changed it
/// (<c>CONTEXT.md</c>, Page revision).
/// </summary>
/// <remarks>
/// <para>
/// <strong>It keeps the title as well as the body.</strong> A rename is a change
/// to the page like any other, and a history that kept only the Markdown would
/// answer "what did this say" and not "what was this called" — which is half of
/// what somebody reading a history is trying to find out.
/// </para>
/// <para>
/// It does not keep the place. Where a page sits is the tree's business and the
/// tree is what the owner is looking at when they wonder; a history that could
/// put a page back somewhere it no longer fits would be a history that can
/// break the hierarchy, and recovering is meant to be the safe operation.
/// </para>
/// <para>
/// <strong>A revision never changes</strong>, which is why it has no version: a
/// guarded write that recovers one holds the <em>page</em>'s
/// (<see cref="Revisions"/>).
/// </para>
/// </remarks>
public sealed class PageRevision : IRevision
{
    private PageRevision()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>The page this is a version of.</summary>
    public Guid PageId { get; private init; }

    /// <summary>What it was called until <see cref="At"/>.</summary>
    public string Title { get; private init; } = string.Empty;

    /// <summary>What it said until <see cref="At"/>.</summary>
    public string Markdown { get; private init; } = string.Empty;

    public DateTimeOffset At { get; private init; }

    public Actor By { get; private init; } = null!;

    /// <summary>What <paramref name="page"/> said until this moment.</summary>
    /// <remarks>
    /// Taken from the page <em>before</em> it is changed, which is the whole of
    /// the convention: the current content lives on the object and never in a
    /// revision, so dropping the oldest is safe to do without looking at
    /// anything else.
    /// </remarks>
    public static PageRevision Of(Page page, Caller by, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        PageId = (page ?? throw new ArgumentNullException(nameof(page))).Id,
        Title = page.Title,
        Markdown = page.Markdown,
        At = at,
        By = Actor.Of(by),
    };
}
