using Personalaffe.Application.Acts.Bookmarks;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Dashboard;

namespace Personalaffe.Application.Acts.Dashboard;

/// <summary>
/// One tile, as a caller sees it: whether the owner has it on the page, and
/// whether this workspace has anything to put in it.
/// </summary>
/// <param name="Offered">
/// Whether it can be shown at all — its application is switched on and this
/// caller may read it. A tile that is not offered keeps whatever the owner set;
/// switching the application back on brings it back as it was, which is why
/// this is a second answer beside <paramref name="Shown"/> rather than a
/// rewriting of it.
/// </param>
public sealed record TheTile(
    DashboardTile Tile, bool Shown, bool Offered, DateTimeOffset UpdatedAt)
{
    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static TheTile Of(TileState state, bool offered) =>
        new(state.Tile, state.Shown, offered, state.UpdatedAt);
}

/// <summary>
/// One Scratchpad entry on the home page: enough of it to recognise, and when
/// the instance will destroy it.
/// </summary>
public sealed record TheLatestEntry(
    Guid Id, string Preview, bool Pinned, DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt);

/// <summary>
/// The home page: which tiles are on it, and what is in the ones that are.
/// </summary>
/// <remarks>
/// A section is <c>null</c> where its tile is not being drawn — hidden, out of
/// this caller's reach, or in a switched-off application — and an empty list
/// where the tile is drawn and there is nothing in it. They are different
/// answers and a client draws them differently: nothing at all, against "you
/// have no open tasks".
/// </remarks>
public sealed record TheDashboard(
    IReadOnlyList<TheTile> Tiles,
    IReadOnlyList<OpenTask>? Tasks,
    IReadOnlyList<RecentPage>? Knowledge,
    IReadOnlyList<TheLatestEntry>? Scratchpad,
    IReadOnlyList<RecentFile>? Files,
    IReadOnlyList<SavedBookmark>? Bookmarks = null);

/// <summary>
/// What is useful or pending right now (VISION §6.1).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One request, because it is one screen.</strong> The home page is the
/// first thing a browser draws and it refreshes on a timer like every other
/// screen; four requests for four tiles would be four times the traffic and four
/// moments at which the tiles could disagree about what this workspace has.
/// </para>
/// <para>
/// <strong>The weather is not in it.</strong> Its tile is in the list — that is
/// how a client knows to draw it — but what it says comes from
/// <c>ReadTheWeather</c> and its own address. A provider somewhere else on the
/// internet must not be able to hold up the owner's home page, and the way to
/// guarantee that is for the two never to be in the same request
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E9).
/// </para>
/// <para>
/// <strong>A tile nobody may see is never asked for.</strong> The permission and
/// the switch are settled first, and only the tiles that survive both are read
/// — so there is no filtering of content here, because content that should not
/// be returned was never fetched. That also makes the answer honest about cost:
/// an agent with Knowledge alone makes one query, not four.
/// </para>
/// </remarks>
public sealed class ReadTheDashboard(
    ICallerIdentity caller,
    IDashboardTiles tiles,
    IDashboard dashboard,
    ReachingAnApplication reaching,
    RetentionSettings retention,
    BookmarkActs bookmarks)
{
    /// <summary>
    /// How many rows a tile carries. Small on purpose: a tile answers "what is
    /// useful or pending", and the application's own screen is one click away
    /// for everything else (VISION §6.1 — an entry point, not a reporting
    /// system).
    /// </summary>
    public const int Rows = 5;

    /// <summary>
    /// How much of a Scratchpad entry a tile carries, in characters. Enough to
    /// recognise what was put down; not so much that five of them are the
    /// Scratchpad screen.
    /// </summary>
    public const int PreviewLength = 280;

    public async Task<TheDashboard> ExecuteAsync(CancellationToken cancellationToken)
    {
        var who = caller.Caller;
        var states = await tiles.ReadAsync(cancellationToken);
        var drawn = new List<TheTile>(states.Count);

        foreach (var state in states)
        {
            drawn.Add(TheTile.Of(state, await OfferedAsync(who, state.Tile, cancellationToken)));
        }

        return new TheDashboard(
            drawn,
            Drawn(drawn, DashboardTile.Tasks)
                ? await dashboard.OpenTasksAsync(Rows, cancellationToken)
                : null,
            Drawn(drawn, DashboardTile.Knowledge)
                ? await dashboard.RecentPagesAsync(Rows, cancellationToken)
                : null,
            Drawn(drawn, DashboardTile.Scratchpad)
                ? [.. (await dashboard.RecentEntriesAsync(Rows, cancellationToken)).Select(Latest)]
                : null,
            Drawn(drawn, DashboardTile.Files)
                ? await dashboard.RecentFilesAsync(Rows, cancellationToken)
                : null,
            Drawn(drawn, DashboardTile.Bookmarks)
                ? await bookmarks.HomeAsync(Rows, cancellationToken)
                : null);
    }

    /// <summary>Whether this tile is being drawn: the owner shows it and it is offered.</summary>
    private static bool Drawn(IReadOnlyList<TheTile> tiles, DashboardTile tile) =>
        tiles.Any(one => one.Tile == tile && one.Shown && one.Offered);

    private TheLatestEntry Latest(RecentEntry entry) => new(
        entry.Id,
        entry.Text.Length > PreviewLength ? entry.Text[..PreviewLength] : entry.Text,
        entry.Pinned,
        entry.UpdatedAt,
        // Worked out here, from the instance's retention, exactly as the
        // Scratchpad's own screen has it (TheEntry): a pinned entry has no
        // expiry at all, which is what pinning means.
        entry.Pinned ? null : entry.UpdatedAt + retention.Scratchpad);

    private Task<bool> OfferedAsync(
        Caller who, DashboardTile tile, CancellationToken cancellationToken) =>
        Offering.ToAsync(who, tile, reaching, cancellationToken);
}

/// <summary>
/// Whether a tile can be drawn at all: its application is switched on and this
/// caller may read it.
/// </summary>
/// <remarks>
/// One function, because two acts answer it and a dashboard whose reading and
/// whose writing disagreed about what "offered" means would be a tile that
/// appears the moment it is hidden.
/// </remarks>
internal static class Offering
{
    public static async Task<bool> ToAsync(
        Caller who,
        DashboardTile tile,
        ReachingAnApplication reaching,
        CancellationToken cancellationToken) =>
        tile.Application() is not { } application
        || (who.Permissions.MayRead(application)
            && await reaching.SwitchedOnAsync(application, cancellationToken));
}

/// <summary>
/// Puts one tile on the home page, or takes it off. The owner's alone.
/// </summary>
/// <remarks>
/// <para>
/// The owner's, because the home page is theirs: an agent has no home page and
/// nothing to do with which compact views the owner keeps on one. It sits with
/// switching an application and issuing a credential on the short list of
/// things agent access deliberately cannot reach
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E2).
/// </para>
/// <para>
/// Guarded like every other write, and hiding a tile that is already hidden
/// still checks the version it was read at — a write that agreed with what is
/// stored is still a write somebody made from a stale screen.
/// </para>
/// <para>
/// A tile that is not offered can still be hidden and shown. The setting is the
/// owner's preference and outlives a switched-off application, so that turning
/// Tasks back on brings the tile back as it was rather than as the default.
/// </para>
/// </remarks>
public sealed class ShowOrHideATile(
    ICallerIdentity caller,
    IDashboardTiles tiles,
    ReachingAnApplication reaching,
    TimeProvider clock)
{
    public async Task<TheTile> ExecuteAsync(
        DashboardTile tile, bool shown, ContentVersion held, CancellationToken cancellationToken)
    {
        var who = caller.Caller.RequireOwner("show or hide a tile on the home page");
        var state = await tiles.ReadAsync(tile, cancellationToken);

        if (!state.Version.Matches(held))
        {
            throw Refusal.Stale(
                "The tile has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.",
                state.Version);
        }

        if (state.Show(shown, clock.GetUtcNow()))
        {
            await tiles.SaveAsync(cancellationToken);
        }

        return TheTile.Of(state, await Offering.ToAsync(who, tile, reaching, cancellationToken));
    }
}
