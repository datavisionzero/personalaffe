using Personalaffe.Domain;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.Application.Ports;

/// <summary>
/// One page as the tree knows it: what it is called, where it sits, and nothing
/// it says.
/// </summary>
/// <remarks>
/// <strong>The body is deliberately not here.</strong> A knowledge base is
/// navigated far more often than any one page is read, and every structural
/// question — what is the tree, what is above this, what is under it, is this
/// title taken — is answered without loading a mebibyte of Markdown per page.
/// The one read that wants bodies says so (<see cref="IPages.EverythingAsync"/>).
/// </remarks>
public sealed record PageInTheTree(
    Guid Id,
    string Title,
    Guid? ParentId,
    bool Deleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Where knowledge pages and their history are kept (<see cref="Page"/>,
/// <see cref="PageRevision"/>).
/// </summary>
/// <remarks>
/// Nothing here takes a caller. Permission and the application switch are
/// settled by the acts, once, through <c>ReachingAnApplication</c>.
/// </remarks>
public interface IPages
{
    /// <summary>The whole tree, deleted pages left out, by title within a parent.</summary>
    Task<IReadOnlyList<PageInTheTree>> TreeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The same, seeing what is in the Trash — what a move, a restore and the
    /// Trash itself ask.
    /// </summary>
    Task<IReadOnlyList<PageInTheTree>> StructureAsync(CancellationToken cancellationToken);

    /// <summary>One page with its Markdown, or nothing. Deleted ones are not found.</summary>
    Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The same, seeing what is in the Trash — what an address answers
    /// <c>deleted</c> from rather than <c>not-found</c>.
    /// </summary>
    Task<Page?> FindEvenDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every live page, with its Markdown. What the export reads.</summary>
    Task<IReadOnlyList<Page>> EverythingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether a page under <paramref name="parent"/> is already called
    /// <paramref name="title"/>, other than <paramref name="itself"/>.
    /// </summary>
    Task<bool> TitleIsTakenAsync(
        Guid? parent, string title, Guid? itself, CancellationToken cancellationToken);

    /// <summary>Puts a new page down.</summary>
    Task AddAsync(Page page, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the change an act has already made, refusing as <c>stale</c> if
    /// the row moved underneath it.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets a page aside with everything under it, under one moment, so that the
    /// whole thing comes back together (<see cref="Restoration"/>). Its
    /// revisions go with it.
    /// </summary>
    Task DeleteAsync(Page page, Caller by, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>What a page used to say, newest first.</summary>
    Task<IReadOnlyList<PageRevision>> RevisionsAsync(Guid page, CancellationToken cancellationToken);

    /// <summary>One revision of one page, or nothing.</summary>
    Task<PageRevision?> FindRevisionAsync(
        Guid page, Guid revision, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps what the page said until now, and drops whatever then falls outside
    /// what is kept (<see cref="Revisions.Kept"/>).
    /// </summary>
    /// <remarks>
    /// It is one call and not two because the second is not optional: a module
    /// that wrote a revision and forgot to drop the superseded ones would grow
    /// without bound, and there is no reason for a caller to be able to.
    /// </remarks>
    Task KeepAsync(PageRevision revision, CancellationToken cancellationToken);
}
