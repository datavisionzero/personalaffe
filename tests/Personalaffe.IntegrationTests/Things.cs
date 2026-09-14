using Microsoft.EntityFrameworkCore;
using Personalaffe.Api.Http;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The proving ground with a tree in it: what a content module with a hierarchy
/// — Files (PERSONAL-E6), Knowledge (PERSONAL-E7) — will look like when it
/// applies PERSONAL-E3's conventions.
/// </summary>
/// <remarks>
/// Every rule here is the production code's: <see cref="Recoverable"/> for the
/// deletion, <see cref="Restoration"/> for what comes back with what,
/// <see cref="GuardedSave"/> for the write. What belongs to the module and
/// stays here is the one thing the conventions deliberately do not know — what
/// the tree is.
/// </remarks>
internal sealed class Things(Func<ProvingGround> open)
{
    public async Task<Guid> AddAsync(string name, Guid? parent, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var thing = new Thing { Id = Guid.CreateVersion7(now), Name = name, ParentId = parent, CreatedAt = now, UpdatedAt = now };

        await using var context = open();
        context.Things.Add(thing);
        await context.SaveChangesAsync(cancellationToken);

        return thing.Id;
    }

    /// <summary>
    /// Deletes it and everything under it, under one moment, so that the whole
    /// thing comes back together.
    /// </summary>
    public async Task DeleteAsync(Guid id, Caller by, CancellationToken cancellationToken)
    {
        await using var context = open();

        var live = await context.Things.ToListAsync(cancellationToken);
        var going = Subtree(live, id);
        var now = DateTimeOffset.UtcNow;

        foreach (var thing in going)
        {
            thing.Delete(by, now);
            thing.UpdatedAt = now;
        }

        await GuardedSave.SaveAsync(context, "The thing", cancellationToken);
    }

    public async Task<RestoredTo> RestoreAsync(
        Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken)
    {
        await using var context = open();

        var all = await context.Things.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var itself = all.Single(thing => thing.Id == id);

        EntityTags.RequireCurrent(itself.Version, held, "The thing");

        var (chain, reachesTheRoot) = ChainFrom(all, itself);
        var destination = reachesTheRoot ? itself.ParentId : null;

        var plan = Restoration.Plan(
            chain,
            reachesTheRoot,
            restoreAs,
            [.. all
                .Where(thing => thing.ParentId == destination && !thing.IsDeleted() && thing.Id != id)
                .Select(thing => thing.Name)]);

        var now = DateTimeOffset.UtcNow;

        // Everything that went with it comes back with it: the subtree shares
        // the moment it was deleted at, which is what makes "the whole thing" a
        // query rather than a guess. The moment is read before anything is
        // restored, because restoring clears it.
        var went = itself.DeletedAt;

        foreach (var beneath in Subtree(all, id).Where(beneath => beneath.DeletedAt == went))
        {
            beneath.Restore();
            beneath.UpdatedAt = now;
        }

        // An ancestor comes back because the thing would otherwise be
        // unreachable, and for no other reason — so it comes back as itself and
        // not with everything it used to contain. Restoring one page is
        // restoring one page.
        foreach (var ancestor in all.Where(
                     thing => thing.Id != id && plan.Restore.Contains(thing.Id)))
        {
            ancestor.Restore();
            ancestor.UpdatedAt = now;
        }

        itself.Name = plan.Name;
        itself.ParentId = plan.Parent;
        itself.UpdatedAt = now;

        await GuardedSave.SaveAsync(context, "The thing", cancellationToken);

        return new RestoredTo(
            WorkspaceApplication.Knowledge,
            id,
            plan.Name,
            plan.Parent is { } parent ? all.Single(thing => thing.Id == parent).Name : null,
            plan.MovedToTheRoot);
    }

    /// <summary>
    /// Changes the content, leaving behind the version it replaced — a guarded
    /// write like any other (<see cref="Revisions"/>).
    /// </summary>
    public async Task EditAsync(
        Guid id, ContentVersion held, string content, Caller by, CancellationToken cancellationToken)
    {
        await using var context = open();

        var thing = await context.Things.SingleAsync(candidate => candidate.Id == id, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        EntityTags.RequireCurrent(thing.Version, held, "The thing");

        context.ThingRevisions.Add(new ThingRevision
        {
            Id = Guid.CreateVersion7(now),
            ThingId = id,
            Content = thing.Content,
            At = now,
            By = Actor.Of(by),
        });

        thing.Content = content;
        thing.UpdatedAt = now;

        await GuardedSave.SaveAsync(context, "The thing", cancellationToken);
        await DropSupersededAsync(id, cancellationToken);
    }

    /// <summary>
    /// Puts an old version back — which writes forward, leaving a revision of
    /// what was current until now.
    /// </summary>
    public async Task RecoverAsync(
        Guid id, Guid revisionId, ContentVersion held, Caller by, CancellationToken cancellationToken)
    {
        await using var context = open();

        var thing = await context.Things.SingleAsync(candidate => candidate.Id == id, cancellationToken);

        EntityTags.RequireCurrent(thing.Version, held, "The thing");

        var revision = await context.ThingRevisions.SingleOrDefaultAsync(
                candidate => candidate.Id == revisionId && candidate.ThingId == id, cancellationToken)
            ?? throw Refusal.NotFound("This thing has no such revision.");

        var now = DateTimeOffset.UtcNow;

        context.ThingRevisions.Add(new ThingRevision
        {
            Id = Guid.CreateVersion7(now),
            ThingId = id,
            Content = thing.Content,
            At = now,
            By = Actor.Of(by),
        });

        thing.Content = revision.Content;
        thing.UpdatedAt = now;

        await GuardedSave.SaveAsync(context, "The thing", cancellationToken);
        await DropSupersededAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<ThingRevision>> RevisionsAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = open();

        return await context.ThingRevisions
            .Where(revision => revision.ThingId == id)
            .OrderByDescending(revision => revision.At)
            .ToListAsync(cancellationToken);
    }

    public async Task<string> ContentAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = open();

        return (await context.Things
            .IgnoreQueryFilters()
            .SingleAsync(thing => thing.Id == id, cancellationToken)).Content;
    }

    /// <summary>
    /// What every module that keeps history does after writing one: drop
    /// whatever now falls outside what is kept.
    /// </summary>
    private async Task DropSupersededAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = open();

        var all = await context.ThingRevisions
            .Where(revision => revision.ThingId == id)
            .ToListAsync(cancellationToken);

        var superseded = Revisions.Superseded(all);

        if (superseded.Count == 0)
        {
            return;
        }

        context.ThingRevisions.RemoveRange(superseded);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// What the owner does when they want something gone now: the row and
    /// everything under it, for good.
    /// </summary>
    public async Task<int> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = open();

        var all = await context.Things.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var itself = all.Single(thing => thing.Id == id);

        // What goes is this Trash entry: the thing, and what was deleted with
        // it. Something below it that the owner deleted separately is a Trash
        // entry of its own, with an expiry of its own, and destroying it as a
        // side effect would destroy something nobody selected.
        var going = Subtree(all, id).Where(thing => thing.DeletedAt == itself.DeletedAt).ToList();

        context.Things.RemoveRange(going);
        await context.SaveChangesAsync(cancellationToken);

        return going.Count;
    }

    /// <summary>What the sweep does: everything deleted on or before the deadline, gone.</summary>
    public async Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken)
    {
        await using var context = open();

        return await context.Things
            .IgnoreQueryFilters()
            .Where(thing => thing.DeletedAt != null && thing.DeletedAt <= expiredBefore)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>What an ordinary read sees, as paths, so a test can say what the tree is.</summary>
    public async Task<IReadOnlyList<string>> PathsAsync(CancellationToken cancellationToken)
    {
        await using var context = open();
        var live = await context.Things.ToListAsync(cancellationToken);

        return [.. live.Select(thing => PathOf(live, thing)).Order(StringComparer.Ordinal)];
    }

    public async Task<Thing> InTheTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = open();

        return await context.Things.IgnoreQueryFilters().SingleAsync(thing => thing.Id == id, cancellationToken);
    }

    /// <summary>
    /// The thing itself, then its ancestors outwards — and whether that chain
    /// reaches the root at all. It does not when an ancestor expired out of the
    /// Trash and was removed for good, which is the one case where the place
    /// something came from no longer exists.
    /// </summary>
    private static (IReadOnlyList<Placement> Chain, bool ReachesTheRoot) ChainFrom(
        IReadOnlyList<Thing> all, Thing itself)
    {
        var chain = new List<Placement>();
        var walking = itself;

        while (true)
        {
            chain.Add(new Placement(walking.Id, walking.Name, walking.IsDeleted()));

            if (walking.ParentId is not { } parentId)
            {
                return (chain, true);
            }

            if (all.SingleOrDefault(thing => thing.Id == parentId) is not { } parent)
            {
                return (chain, false);
            }

            walking = parent;
        }
    }

    private static List<Thing> Subtree(IReadOnlyList<Thing> all, Guid root)
    {
        var found = new List<Thing>();
        var pending = new Queue<Guid>([root]);

        while (pending.TryDequeue(out var id))
        {
            if (all.SingleOrDefault(thing => thing.Id == id) is not { } thing)
            {
                continue;
            }

            found.Add(thing);

            foreach (var child in all.Where(candidate => candidate.ParentId == id))
            {
                pending.Enqueue(child.Id);
            }
        }

        return found;
    }

    private static string PathOf(IReadOnlyList<Thing> live, Thing thing)
    {
        var parts = new List<string>();
        var walking = thing;

        while (true)
        {
            parts.Insert(0, walking.Name);

            if (walking.ParentId is not { } parentId
                || live.SingleOrDefault(candidate => candidate.Id == parentId) is not { } parent)
            {
                return "/" + string.Join("/", parts);
            }

            walking = parent;
        }
    }
}
