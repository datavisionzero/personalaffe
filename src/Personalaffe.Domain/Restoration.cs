namespace Personalaffe.Domain;

/// <summary>
/// One step on the way from a piece of content up to the root of the hierarchy
/// it lives in: itself first, then its folder, then that folder's folder.
/// </summary>
public sealed record Placement(Guid Id, string Name, bool Deleted);

/// <summary>What restoring one thing means for the tree it came out of.</summary>
/// <param name="Restore">
/// Everything that has to come back for this to be reachable: the thing itself,
/// and every ancestor of it that is also in the Trash.
/// </param>
/// <param name="Name">What it is called when it is back.</param>
/// <param name="Parent">Where it goes, or nothing for the root.</param>
/// <param name="MovedToTheRoot">
/// Whether the place it came from is gone for good, so that it is being put
/// somewhere else. The caller is told; nothing moves silently.
/// </param>
public sealed record RestorationPlan(
    IReadOnlyList<Guid> Restore, string Name, Guid? Parent, bool MovedToTheRoot);

/// <summary>
/// The two rules for putting something back into a hierarchy, decided once for
/// Files (PERSONAL-E6) and Knowledge (PERSONAL-E7) rather than twice.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An ancestor in the Trash comes back with it.</strong> The alternative
/// — refusing until the folder above has been restored first — makes the owner
/// walk the tree by hand, and the alternative to that is content sitting
/// somewhere they cannot reach, which is worse than a folder reappearing.
/// </para>
/// <para>
/// <strong>A name already taken is a refusal, not a silent rename.</strong> Two
/// things with one name in one place is a tree nobody can navigate, and a
/// product that quietly appends "(2)" has made a decision the owner would have
/// made differently. The refusal names what is in the way, and the same call
/// takes the name to put it back under, so nobody is ever stuck with something
/// they cannot get out of the Trash.
/// </para>
/// <para>
/// <strong>Deleting a container takes its subtree with it, under one
/// moment</strong>, so the whole thing comes back together. That is the module's
/// to do — it owns the tree — and it is written here because it is the half of
/// this rule that is not a function.
/// </para>
/// <para>
/// <strong>A Trash entry is the thing and what was deleted with it</strong> —
/// one moment, one entry. Something further down that the owner deleted
/// separately is a Trash entry of its own, with an expiry of its own: it does
/// not come back when its folder does, and removing the folder for good does
/// not destroy it either. That is the rule that makes a chain able to break at
/// all, which is what <c>reachesTheRoot</c> is for.
/// </para>
/// <para>
/// <strong>An ancestor comes back as itself</strong>, not with everything it
/// used to contain. It is being restored because the thing below it would
/// otherwise be unreachable, and for no other reason: restoring one page is
/// restoring one page.
/// </para>
/// <para>
/// What this is not is a hierarchy framework. What a folder is belongs to Files
/// and what a page's place is belongs to Knowledge; this takes the chain they
/// hand it and says what comes back.
/// </para>
/// </remarks>
public static class Restoration
{
    /// <summary>
    /// The plan for restoring <c>chain[0]</c>, whose ancestors follow it up
    /// towards the root.
    /// </summary>
    /// <param name="chain">
    /// The thing itself, then its ancestors outwards. Deleted ones included:
    /// that is what this is here to notice.
    /// </param>
    /// <param name="reachesTheRoot">
    /// Whether the chain is whole. False when an ancestor has already expired
    /// out of the Trash and been removed for good, which is the one case where
    /// the place it came from no longer exists.
    /// </param>
    /// <param name="restoreAs">A different name, where the caller asked for one.</param>
    /// <param name="namesInTheWay">
    /// What is already called something in the place it is going, deleted things
    /// excluded — they are not in anybody's way.
    /// </param>
    /// <exception cref="Refusal">
    /// <c>conflict</c>: something already occupies that name.
    /// <c>validation</c>: the name asked for is not a name.
    /// </exception>
    public static RestorationPlan Plan(
        IReadOnlyList<Placement> chain,
        bool reachesTheRoot,
        string? restoreAs,
        IReadOnlyCollection<string> namesInTheWay)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(namesInTheWay);

        if (chain.Count == 0)
        {
            throw new ArgumentException("A chain starts with the thing being restored.", nameof(chain));
        }

        var itself = chain[0];
        var name = Named(restoreAs, itself.Name);

        if (namesInTheWay.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw Refusal.Conflict(
                $"Something called `{name}` is already in the place this came from. Restore it under "
                + "another name, or move what is there first.");
        }

        // The place it came from has been removed for good, so there is nowhere
        // to put it back — except the root, which is the only place that cannot
        // itself have gone.
        if (!reachesTheRoot)
        {
            return new RestorationPlan([itself.Id], name, Parent: null, MovedToTheRoot: true);
        }

        var restore = new List<Guid> { itself.Id };

        restore.AddRange(chain
            .Skip(1)
            .Where(ancestor => ancestor.Deleted)
            .Select(ancestor => ancestor.Id));

        return new RestorationPlan(
            restore,
            name,
            Parent: chain.Count > 1 ? chain[1].Id : null,
            MovedToTheRoot: false);
    }

    private static string Named(string? restoreAs, string itsOwn)
    {
        if (restoreAs is null)
        {
            return itsOwn;
        }

        var asked = restoreAs.Trim();

        return asked.Length == 0
            ? throw Refusal.Validation("name", "A name is not empty.")
            : asked;
    }
}
