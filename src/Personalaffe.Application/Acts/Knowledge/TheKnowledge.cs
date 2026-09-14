using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.Application.Acts.Knowledge;

/// <summary>One page, with what it says.</summary>
public sealed record ThePage(
    Guid Id,
    string Title,
    Guid? Parent,
    string Markdown,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static ThePage Of(Page page) => new(
        page.Id, page.Title, page.ParentId, page.Markdown, page.CreatedAt, page.UpdatedAt);
}

/// <summary>One page as the tree knows it: a title and a place, and no body.</summary>
public sealed record TheOutline(
    Guid Id, string Title, Guid? Parent, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static TheOutline Of(PageInTheTree page) => new(
        page.Id, page.Title, page.ParentId, page.CreatedAt, page.UpdatedAt);
}

/// <summary>
/// The whole hierarchy, flat, with each page saying which one it is under.
/// </summary>
/// <remarks>
/// <strong>Flat and not nested.</strong> A client draws the tree from the parent
/// of each page in one pass, and a nested document would be one a caller has to
/// walk to find anything — including the CLI, which wants a path, and the
/// export, which wants every page once. It is also what makes "the tree" a
/// single value a screen can hold rather than a shape it has to reassemble.
/// </remarks>
public sealed record TheTree(IReadOnlyList<TheOutline> Pages);

/// <summary>One previous version of a page, as a history lists it.</summary>
/// <remarks>
/// No Markdown. A page keeps up to fifty versions and each may be a mebibyte;
/// a history that carried all of them would be a read nobody could afford to
/// make in order to see when something changed. What it said is its own read.
/// </remarks>
public sealed record TheRevision(Guid Id, string Title, DateTimeOffset At, Actor By)
{
    internal static TheRevision Of(PageRevision revision) =>
        new(revision.Id, revision.Title, revision.At, revision.By);
}

/// <summary>One previous version, with what it said.</summary>
public sealed record TheOldVersion(Guid Id, string Title, string Markdown, DateTimeOffset At, Actor By)
{
    internal static TheOldVersion Of(PageRevision revision) => new(
        revision.Id, revision.Title, revision.Markdown, revision.At, revision.By);
}

/// <summary>
/// What the Knowledge acts have in common: the refusals they share, and the
/// three questions every write in a tree has to ask.
/// </summary>
internal static class TheKnowledge
{
    internal static Refusal NoSuchPage(Guid id) =>
        Refusal.NotFound($"Nothing with the id {id} is a page in this workspace.");

    /// <summary>
    /// The page at <paramref name="id"/>, or the refusal its address answers:
    /// <c>deleted</c> while it is in the Trash, <c>not-found</c> otherwise.
    /// </summary>
    internal static async Task<Page> PageAsync(
        IPages pages, RetentionSettings retention, Guid id, CancellationToken cancellationToken)
    {
        var page = await pages.FindEvenDeletedAsync(id, cancellationToken) ?? throw NoSuchPage(id);

        return page.IsDeleted()
            ? throw page.Gone($"The page `{page.Title}`", retention.Trash)
            : page;
    }

    /// <summary>
    /// The place a page is going: it has to exist, be out of the Trash, and
    /// leave room under it.
    /// </summary>
    /// <param name="depthBelow">
    /// How many levels the page brings with it: 0 for a new one, and the height
    /// of its subtree for one being moved.
    /// </param>
    internal static async Task RoomUnderAsync(
        IPages pages,
        RetentionSettings retention,
        Guid? parent,
        int depthBelow,
        CancellationToken cancellationToken)
    {
        if (parent is not { } id)
        {
            return;
        }

        await PageAsync(pages, retention, id, cancellationToken);

        var structure = await pages.StructureAsync(cancellationToken);
        var depth = Depth(structure, id);

        if (depth + depthBelow + 1 > Page.MaxDepth)
        {
            throw Refusal.Conflict(
                $"This workspace's pages go {Page.MaxDepth} deep, and that would be deeper. A knowledge "
                + "base nobody can find anything in is what the limit is for.");
        }
    }

    /// <summary>
    /// Whether a page under this parent is already called that.
    /// </summary>
    /// <exception cref="Refusal">
    /// <c>conflict</c>: one is. Never a silent rename — two pages with one title
    /// in one place is a tree nobody can navigate, and it is two files with one
    /// name when the export writes them out.
    /// </exception>
    internal static async Task TitleIsFreeAsync(
        IPages pages, Guid? parent, string title, Guid? itself, CancellationToken cancellationToken)
    {
        if (await pages.TitleIsTakenAsync(parent, title, itself, cancellationToken))
        {
            throw Refusal.Conflict(
                $"A page there is already called `{title}`. Titles in one place are one each, whatever "
                + "their capitals.");
        }
    }

    /// <summary>The check every guarded write in this module makes.</summary>
    internal static void RequireCurrent(ContentVersion current, ContentVersion held)
    {
        if (!current.Matches(held))
        {
            throw Refusal.Stale(
                "The page has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.",
                current);
        }
    }

    /// <summary>How many pages there are from the top down to this one.</summary>
    internal static int Depth(IReadOnlyList<PageInTheTree> structure, Guid id)
    {
        var depth = 0;
        var walking = (Guid?)id;

        while (walking is { } at)
        {
            if (structure.FirstOrDefault(page => page.Id == at) is not { } page)
            {
                return depth;
            }

            depth++;
            walking = page.ParentId;

            if (depth > Page.MaxDepth)
            {
                throw new InvalidOperationException(
                    $"The page {id} is deeper than {Page.MaxDepth} pages, which nothing can make.");
            }
        }

        return depth;
    }

    /// <summary>
    /// How many levels there are below <paramref name="root"/> — what a page
    /// brings with it when it lands somewhere else.
    /// </summary>
    internal static int Height(IReadOnlyList<PageInTheTree> structure, Guid root)
    {
        var below = structure.Where(page => page.ParentId == root).ToList();

        return below.Count == 0 ? 0 : 1 + below.Max(page => Height(structure, page.Id));
    }

    /// <summary>Whether <paramref name="candidate"/> is in the subtree of <paramref name="root"/>.</summary>
    internal static bool IsUnder(IReadOnlyList<PageInTheTree> structure, Guid root, Guid candidate)
    {
        var walking = (Guid?)candidate;

        for (var step = 0; walking is { } at && step <= Page.MaxDepth; step++)
        {
            if (at == root)
            {
                return true;
            }

            walking = structure.FirstOrDefault(page => page.Id == at)?.ParentId;
        }

        return false;
    }
}
