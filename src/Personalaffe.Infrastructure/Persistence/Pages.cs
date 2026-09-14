using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The knowledge pages and their history in Postgres (<see cref="IPages"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every structural read is a projection.</strong> The tree, the chain
/// above a page, the subtree under it and "is this title taken" all go through
/// <see cref="PageInTheTree"/>, which has no Markdown in it — so navigating a
/// knowledge base costs the same whether its pages are a paragraph or a
/// mebibyte each. Two reads want bodies and say so.
/// </para>
/// <para>
/// The tree is walked in memory, as Files' is: one person's knowledge base is
/// small, it is bounded by <see cref="Page.MaxDepth"/>, and a recursive CTE
/// would put the shape of the hierarchy into SQL where the module could no
/// longer say what it is.
/// </para>
/// </remarks>
public sealed class Pages(PersonalaffeDbContext context) : IPages
{
    public async Task<IReadOnlyList<PageInTheTree>> TreeAsync(CancellationToken cancellationToken) =>
        await context.Pages
            .OrderBy(page => page.Title)
            .Select(page => new PageInTheTree(
                page.Id, page.Title, page.ParentId, false, page.CreatedAt, page.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PageInTheTree>> StructureAsync(CancellationToken cancellationToken) =>
        await context.Pages
            .IgnoreQueryFilters()
            .OrderBy(page => page.Title)
            .Select(page => new PageInTheTree(
                page.Id,
                page.Title,
                page.ParentId,
                page.DeletedAt != null,
                page.CreatedAt,
                page.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Pages.FirstOrDefaultAsync(page => page.Id == id, cancellationToken);

    public async Task<Page?> FindEvenDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Pages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(page => page.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Page>> EverythingAsync(CancellationToken cancellationToken) =>
        await context.Pages.OrderBy(page => page.Title).ToListAsync(cancellationToken);

    public async Task<bool> TitleIsTakenAsync(
        Guid? parent, string title, Guid? itself, CancellationToken cancellationToken)
    {
        // Filtered coarsely in the database and decided here, so that what "the
        // same title" means is Domain's and not a collation's.
        var taken = await context.Pages
            .Where(page => page.ParentId == parent && page.Id != itself)
            .Select(page => page.Title)
            .ToListAsync(cancellationToken);

        return taken.Any(one => PageTitle.Same(one, title));
    }

    public async Task AddAsync(Page page, CancellationToken cancellationToken)
    {
        await context.Pages.AddAsync(page, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The page", cancellationToken);

    public async Task DeleteAsync(
        Page page, Caller by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        // Everything under it goes with it, under one moment, so the whole thing
        // is one Trash entry and comes back together (Restoration). The tracked
        // entities are loaded by id rather than projected, because these are the
        // rows being written.
        var going = Subtree(await StructureAsync(cancellationToken), page.Id)
            .Select(one => one.Id)
            .ToHashSet();

        var pages = await context.Pages
            .Where(candidate => going.Contains(candidate.Id))
            .ToListAsync(cancellationToken);

        foreach (var beneath in pages)
        {
            beneath.Delete(by, at);
            beneath.Touch(at);
        }

        await GuardedSave.SaveAsync(context, "The page", cancellationToken);
    }

    public async Task<IReadOnlyList<PageRevision>> RevisionsAsync(
        Guid page, CancellationToken cancellationToken) =>
        await context.PageRevisions
            .Where(revision => revision.PageId == page)
            .OrderByDescending(revision => revision.At)
            .ThenByDescending(revision => revision.Id)
            .ToListAsync(cancellationToken);

    public async Task<PageRevision?> FindRevisionAsync(
        Guid page, Guid revision, CancellationToken cancellationToken) =>
        await context.PageRevisions.FirstOrDefaultAsync(
            candidate => candidate.Id == revision && candidate.PageId == page, cancellationToken);

    public async Task KeepAsync(PageRevision revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);

        await context.PageRevisions.AddAsync(revision, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // Writing one and dropping what it pushed out are one operation, because
        // the second is not optional: a module that forgot it would grow without
        // bound.
        var all = await context.PageRevisions
            .Where(candidate => candidate.PageId == revision.PageId)
            .ToListAsync(cancellationToken);

        var superseded = Revisions.Superseded(all);

        if (superseded.Count == 0)
        {
            return;
        }

        context.PageRevisions.RemoveRange(superseded);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A page and everything under it, out of a structure somebody has already
    /// read. Which structure decides what "under it" means: the live pages when
    /// something is being deleted, and every page when something is coming back.
    /// </summary>
    internal static List<PageInTheTree> Subtree(IReadOnlyList<PageInTheTree> all, Guid root)
    {
        var found = new List<PageInTheTree>();
        var pending = new Queue<Guid>([root]);

        while (pending.TryDequeue(out var id))
        {
            if (all.FirstOrDefault(page => page.Id == id) is not { } page)
            {
                continue;
            }

            found.Add(page);

            foreach (var child in all.Where(candidate => candidate.ParentId == id))
            {
                pending.Enqueue(child.Id);
            }
        }

        return found;
    }

    /// <summary>
    /// The pages from <paramref name="from"/> up to the top, and whether the
    /// chain reaches it. It does not when an ancestor expired out of the Trash
    /// and was removed for good.
    /// </summary>
    internal static (List<PageInTheTree> Chain, bool ReachesTheRoot) Above(
        IReadOnlyList<PageInTheTree> all, Guid? from)
    {
        var chain = new List<PageInTheTree>();
        var walking = from;

        while (walking is { } id)
        {
            if (all.FirstOrDefault(page => page.Id == id) is not { } page)
            {
                return (chain, false);
            }

            chain.Add(page);
            walking = page.ParentId;

            if (chain.Count > Page.MaxDepth)
            {
                throw new InvalidOperationException(
                    $"The page {from} is deeper than {Page.MaxDepth} pages, which nothing can make.");
            }
        }

        return (chain, true);
    }
}
