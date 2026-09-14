namespace Personalaffe.Domain.Tasks;

/// <summary>
/// A named, manually ordered collection of personal tasks (<c>CONTEXT.md</c>,
/// Task list).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A list is not in another list.</strong> VISION §6.4 asks for
/// "multiple named lists" and rules out project planning; a hierarchy of them
/// would be the first step towards the thing it rules out. So this is the one
/// content module in the product with no tree — nothing here applies
/// <see cref="Restoration"/>, because there is nowhere for something to come
/// back to but where it was.
/// </para>
/// <para>
/// The manual order is the tasks' and not the lists'. Lists are read by name:
/// there are a handful of them, somebody named each one, and a name is how they
/// are asked for from a console.
/// </para>
/// </remarks>
public sealed class TaskList : IRecoverable
{
    private TaskList()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>What the owner called it (<see cref="TaskTitle"/>).</summary>
    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When it was last changed, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Actor? DeletedBy { get; set; }

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>A list made now.</summary>
    /// <exception cref="Refusal"><c>validation</c>: that is not a name.</exception>
    public static TaskList Named(string? name, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = TaskTitle.Accepted(name, "name"),
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Renames it, and says whether that changed anything.</summary>
    /// <exception cref="Refusal"><c>validation</c>: that is not a name.</exception>
    public bool Rename(string? name, DateTimeOffset now)
    {
        var wanted = TaskTitle.Accepted(name, "name");

        if (string.Equals(Name, wanted, StringComparison.Ordinal))
        {
            return false;
        }

        Name = wanted;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Moves the version on for a change the module made itself.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
