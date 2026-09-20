using Personalaffe.Domain;
using Personalaffe.Domain.Search;

namespace Personalaffe.Application.Ports;

/// <summary>
/// One thing the index found, in whichever application it lives in.
/// </summary>
/// <param name="Application">
/// Which of the four it came out of. It decides what the <paramref name="Id"/>
/// is the id of — a page, a task, an entry, a file — because each application
/// contributes exactly one kind of thing and a second word beside this one
/// would have to agree with it.
/// </param>
/// <param name="Title">
/// What to draw as the row's name. Every application has one except the
/// Scratchpad, whose entries have no title at all; there it is the first line
/// of the text, which is what a person recognises an entry by.
/// </param>
/// <param name="Snippet">
/// The piece of the body the words were found in, as <em>text</em> and never as
/// markup: nothing here marks the matched words up, so nothing downstream has
/// to decide whether a snippet is safe to render. A client that wants them
/// marked has the words it asked with. It is nothing where the match was in the
/// title and there is no body to quote.
/// </param>
/// <param name="Within">
/// What it sits in — a page's parent, a task's list, a file's folder — so that
/// a client can open the screen the thing is on rather than the thing alone.
/// Nothing for a Scratchpad entry, which sits in no container, and nothing for
/// anything at the top of its tree.
/// </param>
/// <param name="Rank">
/// How well it matched, between 0 and 1. It is the one number that puts a page
/// and a file name in the same list, and it is the store's to produce: a title
/// match outranks a body match because the index says so, not because something
/// downstream reordered it.
/// </param>
public sealed record Found(
    WorkspaceApplication Application,
    Guid Id,
    string Title,
    string? Snippet,
    Guid? Within,
    DateTimeOffset UpdatedAt,
    double Rank,
    string? TargetUrl = null);

/// <summary>
/// The index over what the owner has written (<see cref="Needle"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One application at a time.</strong> Whether an application may be
/// searched at all is two questions — this caller's permission and the owner's
/// switch — and both are settled by the act, once, through
/// <c>ReachingAnApplication</c>, exactly as every other read in this product
/// settles them. A port that searched all four and filtered afterwards would be
/// a second place that decides what read access means, and the wrong place: it
/// would have read the content first.
/// </para>
/// <para>
/// <strong>What is deleted is not found.</strong> Content in the Trash has left
/// ordinary reads (<c>IRecoverable</c>) and a search is an ordinary read.
/// </para>
/// </remarks>
public interface ISearch
{
    /// <summary>
    /// What <paramref name="application"/> has that matches
    /// <paramref name="needle"/>, best match first, at most
    /// <paramref name="limit"/> of them.
    /// </summary>
    Task<IReadOnlyList<Found>> FindAsync(
        WorkspaceApplication application,
        Needle needle,
        int limit,
        CancellationToken cancellationToken);
}
