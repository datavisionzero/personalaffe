namespace Personalaffe.Domain.Dashboard;

/// <summary>
/// Whether the owner has one <see cref="DashboardTile"/> on their home page.
/// </summary>
/// <remarks>
/// <para>
/// A row per tile, seeded by the migration that creates the table, exactly as
/// the application switch is (<see cref="ApplicationState"/>) and for the same
/// reason: every read is about one tile, the one write changes one of them, and
/// a read never has to decide what a missing row would have meant.
/// </para>
/// <para>
/// <strong>Hidden is a preference and not a permission.</strong> Hiding the
/// Tasks tile does not hide Tasks: the application is still switched on, its
/// screen is still there, and one search still finds what is in it. What it
/// changes is one screen, which is why this is the owner's own setting and not
/// something an agent can reach.
/// </para>
/// </remarks>
public sealed class TileState
{
    private TileState()
    {
    }

    /// <summary>Which tile this is. The key of the row.</summary>
    public DashboardTile Tile { get; private init; }

    /// <summary>Whether the owner has it on the home page.</summary>
    public bool Shown { get; private set; }

    /// <summary>When it was last shown or hidden, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>A tile as a fresh instance has it: shown.</summary>
    /// <remarks>
    /// Shown, for the reason an application is switched on
    /// (<see cref="ApplicationState.Fresh"/>): an instance nobody has
    /// configured is a whole workspace, not an empty page with five things to
    /// find and turn on.
    /// </remarks>
    public static TileState Fresh(DashboardTile tile, DateTimeOffset now) =>
        new() { Tile = tile, Shown = true, UpdatedAt = now };

    /// <summary>
    /// Shows or hides it, and says whether that changed anything — setting a
    /// tile to what it already is is not a write.
    /// </summary>
    public bool Show(bool shown, DateTimeOffset now)
    {
        if (Shown == shown)
        {
            return false;
        }

        Shown = shown;
        UpdatedAt = now;

        return true;
    }
}
