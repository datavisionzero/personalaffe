namespace Personalaffe.Domain.Dashboard;

/// <summary>
/// The compact views of useful or pending information the home page can show
/// (<c>CONTEXT.md</c>, Dashboard tile).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A closed set, in a fixed layout.</strong> VISION.md asks for a
/// predefined dashboard whose tiles can be shown or hidden individually, and
/// says that free arrangement comes later. So this is an enum and not a table
/// of rows somebody can add to: the order below is the order on the screen, a
/// sixth tile is a decision somebody makes and a migration, and there is
/// nothing here for a dashboard builder to grow out of.
/// </para>
/// <para>
/// <strong>Four of them are an application's and the fifth is nobody's.</strong>
/// A tile over an application is offered only while that application is
/// switched on and this caller can read it (<see cref="Belongs.Application"/>).
/// The weather belongs to no application, cannot be switched off with one, and
/// is the only tile whose content does not come out of this instance's own
/// database.
/// </para>
/// </remarks>
public enum DashboardTile
{
    /// <summary>What is open, soonest due first: the first question the home page answers.</summary>
    Tasks,

    /// <summary>The pages that were written in most recently.</summary>
    Knowledge,

    /// <summary>What was put down last and is still there.</summary>
    Scratchpad,

    /// <summary>What arrived on the disk most recently.</summary>
    Files,

    /// <summary>What it is doing where the owner said, from outside this instance.</summary>
    Weather,
}

/// <summary>
/// Which application a tile draws from, where it draws from one.
/// </summary>
/// <remarks>
/// Named for the question rather than for the type, because the word
/// <em>tiles</em> is taken twice over: by the store that keeps their settings
/// and by the spelling the contract uses in an address.
/// </remarks>
public static class Belongs
{
    /// <summary>
    /// The application whose switch and permission decide whether this tile is
    /// offered, or nothing for a tile that is nobody's.
    /// </summary>
    public static WorkspaceApplication? Application(this DashboardTile tile) => tile switch
    {
        DashboardTile.Tasks => WorkspaceApplication.Tasks,
        DashboardTile.Knowledge => WorkspaceApplication.Knowledge,
        DashboardTile.Scratchpad => WorkspaceApplication.Scratchpad,
        DashboardTile.Files => WorkspaceApplication.Files,
        DashboardTile.Weather => null,
        _ => throw new ArgumentOutOfRangeException(
            nameof(tile), tile, "A tile that is neither an application's nor nobody's."),
    };
}
