using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>A page of the Trash, and whether the limit cut it short.</summary>
public sealed record TheTrash(IReadOnlyList<TrashEntry> Items, bool HasMore);

/// <summary>
/// Everything the caller deleted and may still see, one list over the four
/// applications.
/// </summary>
/// <remarks>
/// <para>
/// The fan-out is over whatever contributors are registered, and the filter is
/// the caller's own read access: an application an agent cannot read does not
/// appear, is not counted, and does not reduce what the agent does see. An
/// application the owner has switched off is left out the same way — the Trash
/// is an aggregate view, and a switched-off application is not in one
/// (PERSONAL-E4). What it holds is kept, and the sweep goes on emptying it.
/// </para>
/// <para>
/// <strong>There is no cursor.</strong> A personal Trash holds at most one
/// person's deletions over one retention period, and a limit with
/// <c>has_more</c> is what keeps a runaway from becoming an unbounded response.
/// A cursor over a merged fan-out would be four cursors and a tie-break rule,
/// for a list that fits on a screen.
/// </para>
/// </remarks>
public sealed class ReadTheTrash(
    ICallerIdentity caller, IEnumerable<ITrash> contributors, ReachingAnApplication reaching)
{
    public const int DefaultLimit = 200;

    public const int MaxLimit = 1000;

    public async Task<TheTrash> ExecuteAsync(
        WorkspaceApplication? only, int? limit, CancellationToken cancellationToken)
    {
        var wanted = Limit(limit);
        var who = caller.Caller;

        var candidates = contributors
            .Where(contributor => only is null || contributor.Application == only)
            .Where(contributor => who.Permissions.MayRead(contributor.Application))
            .ToArray();

        var asked = new List<ITrash>(candidates.Length);

        foreach (var contributor in candidates)
        {
            if (await reaching.SwitchedOnAsync(contributor.Application, cancellationToken))
            {
                asked.Add(contributor);
            }
        }

        // One more than asked for, from each, so that "there is more" is a fact
        // rather than a guess about whether the merge cut something off.
        var gathered = new List<TrashEntry>();

        foreach (var contributor in asked)
        {
            gathered.AddRange(await contributor.ListAsync(wanted + 1, cancellationToken));
        }

        var ordered = gathered
            .OrderByDescending(entry => entry.DeletedAt)
            .ThenBy(entry => entry.Application)
            .ThenBy(entry => entry.Id)
            .ToArray();

        return new TheTrash([.. ordered.Take(wanted)], ordered.Length > wanted);
    }

    private static int Limit(int? asked) => asked switch
    {
        null => DefaultLimit,
        < 1 => throw Refusal.Validation("limit", "A limit is at least 1."),
        > MaxLimit => throw Refusal.Validation("limit", $"A limit is at most {MaxLimit}."),
        _ => asked.Value,
    };
}

/// <summary>
/// Puts one thing back where it came from.
/// </summary>
/// <remarks>
/// Restoring is a write, so it needs write access to the application and the
/// version the caller read, like every other write. It is not the owner's
/// alone: an agent that may change an application may undo a deletion in it.
/// </remarks>
public sealed class RestoreFromTheTrash(IEnumerable<ITrash> contributors, ReachingAnApplication reaching)
{
    public async Task<RestoredTo> ExecuteAsync(
        WorkspaceApplication application,
        Guid id,
        ContentVersion held,
        string? restoreAs,
        CancellationToken cancellationToken)
    {
        // Putting something back into an application that is switched off would
        // be a write nobody can see the result of.
        await reaching.ToWriteAsync(application, cancellationToken);

        var contributor = Trash.Of(contributors, application);

        return contributor is null
            ? throw Trash.NoSuchEntry(application, id)
            : await contributor.RestoreAsync(id, held, restoreAs, cancellationToken)
              ?? throw Trash.NoSuchEntry(application, id);
    }
}

/// <summary>
/// Removes one thing for good.
/// </summary>
/// <remarks>
/// <strong>The owner's alone.</strong> An agent that could permanently remove
/// one entry could bypass the Trash in two steps instead of one, and the point
/// of the Trash is that an agent acting on the owner's behalf cannot destroy
/// the owner's content (PERSONAL-E2). `Caller.RequireOwner` names this act in
/// so many words.
/// </remarks>
public sealed class RemoveFromTheTrash(
    ICallerIdentity caller, IEnumerable<ITrash> contributors, ReachingAnApplication reaching)
{
    public async Task ExecuteAsync(
        WorkspaceApplication application, Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("remove something from the Trash for good");

        if (!await reaching.SwitchedOnAsync(application, cancellationToken))
        {
            throw ApplicationState.Off(application);
        }

        var contributor = Trash.Of(contributors, application);

        if (contributor is null || !await contributor.RemoveAsync(id, held, cancellationToken))
        {
            throw Trash.NoSuchEntry(application, id);
        }
    }
}

/// <summary>
/// Empties it. The owner's alone, for the same reason.
/// </summary>
/// <remarks>
/// A switched-off application is left out of an "empty everything" and refuses
/// an "empty this one", like every other operation in it. Its content is kept
/// and its deadlines go on running: switching an application off is not a way
/// to make the owner's deletions permanent early, and not a way to delay them.
/// </remarks>
public sealed class EmptyTheTrash(
    ICallerIdentity caller, IEnumerable<ITrash> contributors, ReachingAnApplication reaching)
{
    public async Task<int> ExecuteAsync(WorkspaceApplication? only, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("empty the Trash");

        if (only is { } one && !await reaching.SwitchedOnAsync(one, cancellationToken))
        {
            throw ApplicationState.Off(one);
        }

        var removed = 0;

        foreach (var contributor in contributors.Where(c => only is null || c.Application == only))
        {
            if (await reaching.SwitchedOnAsync(contributor.Application, cancellationToken))
            {
                removed += await contributor.EmptyAsync(cancellationToken);
            }
        }

        return removed;
    }
}

/// <summary>What the four acts above have in common, and nothing more.</summary>
internal static class Trash
{
    internal static ITrash? Of(IEnumerable<ITrash> contributors, WorkspaceApplication application) =>
        contributors.FirstOrDefault(contributor => contributor.Application == application);

    /// <summary>
    /// One sentence for "this application has no such entry" and for "this
    /// application has no Trash yet", because from outside they are the same
    /// answer — and telling them apart would say which modules this build has.
    /// </summary>
    internal static Refusal NoSuchEntry(WorkspaceApplication application, Guid id) =>
        Refusal.NotFound(
            $"Nothing with the id {id} is in the {application.ToString().ToLowerInvariant()} Trash. It may "
            + "have been restored already, or removed for good, or expired.");
}
