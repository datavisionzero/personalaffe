using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.Application.Acts.Knowledge;

/// <summary>
/// The whole tree: every page's title and place, and nothing any of them says.
/// </summary>
/// <remarks>
/// <strong>No limit and no cursor.</strong> A knowledge base is one person's
/// and is navigated as a whole; a page of a tree is a shape nobody can draw a
/// hierarchy from. What keeps it bounded is that it carries no bodies
/// (<see cref="PageInTheTree"/>) and that the tree is eight deep.
/// </remarks>
public sealed class ReadTheTree(ReachingAnApplication reaching, IPages pages)
{
    public async Task<TheTree> ExecuteAsync(CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Knowledge, cancellationToken);

        return new TheTree([.. (await pages.TreeAsync(cancellationToken)).Select(TheOutline.Of)]);
    }
}

/// <summary>One page, with its Markdown, by its own address.</summary>
/// <remarks>
/// <strong>This address is the whole of what "stable links" means.</strong> It
/// is the page's id, made once when it is written, and it goes on answering
/// through every rename, every move, every rewrite and every recovery
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E7).
/// </remarks>
public sealed class ReadAPage(
    ReachingAnApplication reaching, IPages pages, RetentionSettings retention)
{
    public async Task<ThePage> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Knowledge, cancellationToken);

        return ThePage.Of(await TheKnowledge.PageAsync(pages, retention, id, cancellationToken));
    }
}

/// <summary>A page written for the first time.</summary>
public sealed class WriteANewPage(
    ReachingAnApplication reaching,
    IPages pages,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task<ThePage> ExecuteAsync(
        string? title, Guid? parent, string? markdown, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Knowledge, cancellationToken);

        var accepted = PageTitle.Accepted(title);

        await TheKnowledge.RoomUnderAsync(pages, retention, parent, depthBelow: 0, cancellationToken);
        await TheKnowledge.TitleIsFreeAsync(pages, parent, accepted, itself: null, cancellationToken);

        var page = Page.Written(accepted, parent, markdown, clock.GetUtcNow());

        await pages.AddAsync(page, cancellationToken);

        // No revision. There is nothing it replaced, and a history whose first
        // entry is "it was empty" would be a history with a lie at the bottom.
        return ThePage.Of(page);
    }
}

/// <summary>
/// Changes a page's title, its place, its Markdown, or any of them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One write and not three</strong>, as the Scratchpad's one <c>PUT</c>
/// established and Files kept: a rename, a move and an edit are changes to one
/// row, and three addresses would be three places the guard has to be got right
/// — and three revisions where the owner made one change.
/// </para>
/// <para>
/// <strong>What it replaced is kept</strong> (<see cref="Revisions"/>), unless
/// nothing changed: a write that asked for what is already stored is still
/// guarded and still checked, but it leaves no revision, because a history of
/// moments when nothing happened is a history nobody can read.
/// </para>
/// </remarks>
public sealed class RewriteAPage(
    ReachingAnApplication reaching,
    IPages pages,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task<ThePage> ExecuteAsync(
        Guid id,
        string? title,
        Guid? parent,
        string? markdown,
        ContentVersion held,
        CancellationToken cancellationToken)
    {
        var who = await reaching.ToWriteAsync(WorkspaceApplication.Knowledge, cancellationToken);
        var page = await TheKnowledge.PageAsync(pages, retention, id, cancellationToken);

        TheKnowledge.RequireCurrent(page.Version, held);

        var accepted = PageTitle.Accepted(title);
        var structure = await pages.StructureAsync(cancellationToken);

        if (parent is { } destination && TheKnowledge.IsUnder(structure, id, destination))
        {
            throw Refusal.Conflict(
                destination == id
                    ? $"A page cannot be put under itself. `{page.Title}` is where it already is."
                    : $"`{page.Title}` cannot be put under something that is already under it.");
        }

        await TheKnowledge.RoomUnderAsync(
            pages, retention, parent, TheKnowledge.Height(structure, id), cancellationToken);
        await TheKnowledge.TitleIsFreeAsync(pages, parent, accepted, id, cancellationToken);

        var now = clock.GetUtcNow();

        // Taken before the change, which is the whole of the convention: what is
        // current lives on the page and never in a revision.
        var replaced = PageRevision.Of(page, who, now);

        if (!page.Write(accepted, parent, markdown, now))
        {
            return ThePage.Of(page);
        }

        await pages.SaveAsync(cancellationToken);
        await pages.KeepAsync(replaced, cancellationToken);

        return ThePage.Of(page);
    }
}

/// <summary>
/// Sets a page aside, with everything under it and all of its history.
/// </summary>
/// <remarks>
/// A revision that outlived its page would be content the owner believes they
/// deleted (<see cref="Revisions"/>), so the history goes into the Trash with
/// the page and comes back with it.
/// </remarks>
public sealed class DiscardAPage(
    ReachingAnApplication reaching,
    IPages pages,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task ExecuteAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        var who = await reaching.ToWriteAsync(WorkspaceApplication.Knowledge, cancellationToken);
        var page = await TheKnowledge.PageAsync(pages, retention, id, cancellationToken);

        TheKnowledge.RequireCurrent(page.Version, held);

        await pages.DeleteAsync(page, who, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>What a page used to say, newest first.</summary>
public sealed class ReadTheHistory(
    ReachingAnApplication reaching, IPages pages, RetentionSettings retention)
{
    public async Task<IReadOnlyList<TheRevision>> ExecuteAsync(
        Guid id, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Knowledge, cancellationToken);
        await TheKnowledge.PageAsync(pages, retention, id, cancellationToken);

        return [.. (await pages.RevisionsAsync(id, cancellationToken)).Select(TheRevision.Of)];
    }
}

/// <summary>One previous version, with what it said.</summary>
public sealed class ReadAnOldVersion(
    ReachingAnApplication reaching, IPages pages, RetentionSettings retention)
{
    public async Task<TheOldVersion> ExecuteAsync(
        Guid id, Guid revision, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Knowledge, cancellationToken);
        await TheKnowledge.PageAsync(pages, retention, id, cancellationToken);

        var found = await pages.FindRevisionAsync(id, revision, cancellationToken)
            ?? throw Refusal.NotFound("This page has no such previous version.");

        return TheOldVersion.Of(found);
    }
}

/// <summary>
/// Puts a previous version back.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It writes forward.</strong> What was current until now becomes a
/// revision of its own, so history only grows and nothing is ever destroyed by
/// recovering. Rewinding — dropping everything after the version being put back
/// — would make "undo" the one operation in this product that loses work
/// (<see cref="Revisions"/>).
/// </para>
/// <para>
/// <strong>The version it is guarded by is the page's and not the
/// revision's.</strong> A revision never changes and has no version worth
/// holding; what the caller has to be holding is the page's, or recovering
/// something read ten minutes ago would discard an edit made five minutes ago.
/// </para>
/// <para>
/// It puts back the title as well as the body, and leaves the page where it is.
/// A history that could move a page would be a history that can break the tree.
/// </para>
/// </remarks>
public sealed class RecoverARevision(
    ReachingAnApplication reaching,
    IPages pages,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task<ThePage> ExecuteAsync(
        Guid id, Guid revision, ContentVersion held, CancellationToken cancellationToken)
    {
        var who = await reaching.ToWriteAsync(WorkspaceApplication.Knowledge, cancellationToken);
        var page = await TheKnowledge.PageAsync(pages, retention, id, cancellationToken);

        TheKnowledge.RequireCurrent(page.Version, held);

        var found = await pages.FindRevisionAsync(id, revision, cancellationToken)
            ?? throw Refusal.NotFound("This page has no such previous version.");

        // The title it is coming back under may be taken by a sibling written
        // since. The owner is told, rather than ending up with two pages nobody
        // can tell apart.
        await TheKnowledge.TitleIsFreeAsync(pages, page.ParentId, found.Title, id, cancellationToken);

        var now = clock.GetUtcNow();
        var replaced = PageRevision.Of(page, who, now);

        page.Recover(found.Title, found.Markdown, now);

        await pages.SaveAsync(cancellationToken);
        await pages.KeepAsync(replaced, cancellationToken);

        return ThePage.Of(page);
    }
}
