namespace Personalaffe.Domain;

/// <summary>
/// What a caller may do, one answer per application
/// (<see cref="WorkspaceApplication"/>).
/// </summary>
/// <remarks>
/// Named members keep the wire contract explicit. Omitted bookmark access is
/// none, so credentials issued before this application existed gain nothing.
/// </remarks>
public sealed record Permissions(
    Permission Scratchpad,
    Permission Knowledge,
    Permission Tasks,
    Permission Files,
    Permission Bookmarks = Permission.None)
{
    /// <summary>An agent that has been given nothing yet.</summary>
    public static Permissions None { get; } =
        new(Permission.None, Permission.None, Permission.None, Permission.None);

    /// <summary>The owner's, which is everything, everywhere.</summary>
    public static Permissions Full { get; } =
        new(Permission.ReadWrite, Permission.ReadWrite, Permission.ReadWrite, Permission.ReadWrite, Permission.ReadWrite);

    /// <summary>The answer for one application.</summary>
    public Permission For(WorkspaceApplication application) => application switch
    {
        WorkspaceApplication.Scratchpad => Scratchpad,
        WorkspaceApplication.Knowledge => Knowledge,
        WorkspaceApplication.Tasks => Tasks,
        WorkspaceApplication.Files => Files,
        WorkspaceApplication.Bookmarks => Bookmarks,
        _ => throw new ArgumentOutOfRangeException(
            nameof(application), application, "An application without a permission."),
    };

    /// <summary>Whether this caller may read <paramref name="application"/>.</summary>
    public bool MayRead(WorkspaceApplication application) => For(application) != Permission.None;

    /// <summary>Whether this caller may change <paramref name="application"/>.</summary>
    public bool MayWrite(WorkspaceApplication application) => For(application) == Permission.ReadWrite;
}
