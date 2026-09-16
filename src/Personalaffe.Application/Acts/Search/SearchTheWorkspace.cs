using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Search;

namespace Personalaffe.Application.Acts.Search;

/// <summary>
/// What one search found: the needle as it was read, what matched, and whether
/// the limit cut it short.
/// </summary>
public sealed record TheFindings(string Needle, IReadOnlyList<Found> Items, bool HasMore);

/// <summary>
/// One search over the four applications
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E9).
/// </summary>
/// <remarks>
/// <para>
/// <strong>An application this caller cannot see is left out, not refused.</strong>
/// This is an aggregate view and it behaves like the one this product already
/// has: <c>ReadTheTrash</c> filters its contributors by the caller's read
/// access and by the switch, and naming one application narrows that filter
/// rather than turning the request into a question about permission. So an
/// agent with Knowledge only asks one question and gets pages; an application
/// the owner has switched off contributes nothing; and neither produces a
/// refusal for something the caller did not ask about.
/// </para>
/// <para>
/// <strong>The ordering is the index's.</strong> Every application answers with
/// a rank, and the merge is one sort over the four answers: best match first,
/// most recently changed first where two matched equally well. Nothing here
/// prefers an application for being one — a page does not outrank a task
/// because it is a page.
/// </para>
/// <para>
/// <strong>What is deleted, expired or unreadable never reaches the sort.</strong>
/// The store leaves the Trash out of every statement, the Scratchpad's sweep has
/// already destroyed what expired, and an application nobody may read is never
/// asked. There is no filtering of results here, because results that should
/// not exist were never fetched.
/// </para>
/// </remarks>
public sealed class SearchTheWorkspace(
    ICallerIdentity caller, ISearch search, ReachingAnApplication reaching)
{
    /// <summary>How many findings come back when nobody says.</summary>
    /// <remarks>
    /// Twenty: enough that a whole-workspace search shows something from more
    /// than one application, few enough to be a list somebody reads rather than
    /// scrolls.
    /// </remarks>
    public const int DefaultLimit = 20;

    /// <summary>The most that may be asked for at once.</summary>
    public const int MaxLimit = 100;

    public async Task<TheFindings> ExecuteAsync(
        string? typed,
        WorkspaceApplication? only,
        int? limit,
        CancellationToken cancellationToken)
    {
        var needle = Needle.Of(typed);
        var wanted = Limit(limit);
        var who = caller.Caller;

        var candidates = Enum.GetValues<WorkspaceApplication>()
            .Where(application => only is null || application == only)
            .Where(application => who.Permissions.MayRead(application))
            .ToArray();

        var asked = new List<WorkspaceApplication>(candidates.Length);

        foreach (var application in candidates)
        {
            if (await reaching.SwitchedOnAsync(application, cancellationToken))
            {
                asked.Add(application);
            }
        }

        // One more than asked for, from each, so that "there is more" is a fact
        // about the merge rather than a guess about which application ran out.
        var gathered = new List<Found>();

        foreach (var application in asked)
        {
            gathered.AddRange(await search.FindAsync(application, needle, wanted + 1, cancellationToken));
        }

        var ordered = gathered
            .OrderByDescending(one => one.Rank)
            .ThenByDescending(one => one.UpdatedAt)
            .ThenBy(one => one.Application)
            .ThenBy(one => one.Id)
            .ToArray();

        return new TheFindings(needle.Text, [.. ordered.Take(wanted)], ordered.Length > wanted);
    }

    private static int Limit(int? asked) => asked switch
    {
        null => DefaultLimit,
        < 1 => throw Refusal.Validation("limit", "A limit is at least 1."),
        > MaxLimit => throw Refusal.Validation("limit", $"A limit is at most {MaxLimit}."),
        _ => asked.Value,
    };
}
