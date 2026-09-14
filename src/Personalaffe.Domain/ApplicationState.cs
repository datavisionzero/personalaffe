namespace Personalaffe.Domain;

/// <summary>
/// Whether one <see cref="WorkspaceApplication"/> is part of this workspace
/// today (<c>CONTEXT.md</c>, Application).
/// </summary>
/// <remarks>
/// <para>
/// A row per application rather than four columns on one row, because that is
/// the shape the switch is used in: every read is about one application, and
/// the one write changes one of them. The four rows exist from the migration
/// that creates the table, so a read never has to decide what a missing row
/// would have meant.
/// </para>
/// <para>
/// <strong>Disabling hides an application; it removes nothing.</strong> What
/// was written stays written, the Trash keeps what was deleted, and the sweep
/// goes on emptying it — a deadline that stopped while an application was off
/// would be a way to keep expired content for ever, and content that vanished
/// on re-enabling would be the same surprise from the other side
/// (<c>docs/mvp-plan.md</c>, PERSONAL-E3 and PERSONAL-E4).
/// </para>
/// </remarks>
public sealed class ApplicationState
{
    private ApplicationState()
    {
    }

    /// <summary>Which of the four this is. The key of the row.</summary>
    public WorkspaceApplication Application { get; private init; }

    /// <summary>Whether the owner has it switched on.</summary>
    public bool Enabled { get; private set; }

    /// <summary>When it was last switched, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>An application as a fresh instance has it: on.</summary>
    /// <remarks>
    /// On, because an instance nobody has configured is a whole workspace, not
    /// four switches to find before anything can be written down.
    /// </remarks>
    public static ApplicationState Fresh(WorkspaceApplication application, DateTimeOffset now) =>
        new() { Application = application, Enabled = true, UpdatedAt = now };

    /// <summary>
    /// Switches it, and says whether that changed anything — switching an
    /// application to the state it is already in is not a write.
    /// </summary>
    public bool Switch(bool enabled, DateTimeOffset now)
    {
        if (Enabled == enabled)
        {
            return false;
        }

        Enabled = enabled;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// The refusal every operation in a switched-off application makes.
    /// </summary>
    /// <remarks>
    /// Its own code rather than <c>not-found</c>, because the caller can act on
    /// it: the content is there and the owner can have it back by switching the
    /// application on. Saying "nothing at that address" would send somebody
    /// looking for content that was never lost.
    /// </remarks>
    public static Refusal Off(WorkspaceApplication application)
    {
        // The word rather than the value: an extension member is read by a
        // client, and the four names are one word each, so the spelling here is
        // the spelling the contract uses everywhere else.
        var named = application.ToString().ToLowerInvariant();

        return new Refusal(
            RefusalCode.Disabled,
            $"{named} is switched off in this workspace. What is in it is kept and is not reachable "
            + "until the owner switches it on again.",
            new Dictionary<string, object?> { ["application"] = named });
    }
}
