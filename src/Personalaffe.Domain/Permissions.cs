namespace Personalaffe.Domain;

/// <summary>
/// What a caller may do, one answer per application
/// (<see cref="WorkspaceApplication"/>).
/// </summary>
/// <remarks>
/// Four named members rather than a map, because it is a closed set of four and
/// the contract is better for saying so: a generated client gets four fields it
/// can see, not a dictionary it has to know the keys of.
/// </remarks>
public sealed record Permissions(
    Permission Scratchpad,
    Permission Knowledge,
    Permission Tasks,
    Permission Files)
{
    /// <summary>An agent that has been given nothing yet.</summary>
    public static Permissions None { get; } =
        new(Permission.None, Permission.None, Permission.None, Permission.None);

    /// <summary>The owner's, which is everything, everywhere.</summary>
    public static Permissions Full { get; } =
        new(Permission.ReadWrite, Permission.ReadWrite, Permission.ReadWrite, Permission.ReadWrite);

    /// <summary>The answer for one application.</summary>
    public Permission For(WorkspaceApplication application) => application switch
    {
        WorkspaceApplication.Scratchpad => Scratchpad,
        WorkspaceApplication.Knowledge => Knowledge,
        WorkspaceApplication.Tasks => Tasks,
        WorkspaceApplication.Files => Files,
        _ => throw new ArgumentOutOfRangeException(
            nameof(application), application, "An application without a permission."),
    };

    /// <summary>Whether this caller may read <paramref name="application"/>.</summary>
    public bool MayRead(WorkspaceApplication application) => For(application) != Permission.None;

    /// <summary>Whether this caller may change <paramref name="application"/>.</summary>
    public bool MayWrite(WorkspaceApplication application) => For(application) == Permission.ReadWrite;
}
