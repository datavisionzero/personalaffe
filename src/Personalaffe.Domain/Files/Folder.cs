namespace Personalaffe.Domain.Files;

/// <summary>
/// A named container for files and other folders (<c>CONTEXT.md</c>, Folder).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The root is not a row.</strong> A folder whose <see cref="ParentId"/>
/// is nothing is at the top, and the top itself has no name, no version and
/// nothing to delete. A row standing for it would be a row that can be renamed,
/// moved into itself or set aside, and every one of those is a state this
/// product would then have to have an answer for.
/// </para>
/// <para>
/// <strong>It is recoverable</strong> (<see cref="IRecoverable"/>), and deleting
/// one takes its subtree with it under one moment, so the whole thing comes back
/// together (<see cref="Restoration"/>). The module owns that walk; what is here
/// is one folder.
/// </para>
/// </remarks>
public sealed class Folder : IRecoverable
{
    /// <summary>
    /// How deep the tree goes: the root's children are at 1.
    /// </summary>
    /// <remarks>
    /// VISION §14.4 leaves the number to this epic. Thirty-two is deeper than
    /// anybody organises and shallow enough that every walk of the tree — a
    /// restore working out its chain, a listing working out a breadcrumb, the
    /// CLI resolving a path — is bounded by something other than hope. Without
    /// a limit, a folder moved under its own descendant is a cycle, and a cycle
    /// is a walk that never ends.
    /// </remarks>
    public const int MaxDepth = 32;

    private Folder()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>What the owner called it (<see cref="FileName"/>).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The folder it is in, or nothing for one at the top.</summary>
    public Guid? ParentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When it was last changed, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Actor? DeletedBy { get; set; }

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>A folder made now.</summary>
    /// <exception cref="Refusal"><c>validation</c>: that is not a name.</exception>
    public static Folder Make(string? name, Guid? parent, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = FileName.Accepted(name),
        ParentId = parent,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Renames it, moves it, or both, and says whether that changed anything.
    /// </summary>
    /// <remarks>
    /// One act rather than two, for the reason the Scratchpad's one write gives:
    /// a rename and a move are both changes to the row, and a second address
    /// would be a second place the guard has to be got right. Where it goes is
    /// the module's to check — a folder cannot go inside itself — and what it is
    /// called is here.
    /// </remarks>
    /// <exception cref="Refusal"><c>validation</c>: that is not a name.</exception>
    public bool Change(string? name, Guid? parent, DateTimeOffset now)
    {
        var wanted = FileName.Accepted(name);

        if (string.Equals(Name, wanted, StringComparison.Ordinal) && ParentId == parent)
        {
            return false;
        }

        Name = wanted;
        ParentId = parent;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Moves the version on for a change the module made itself.</summary>
    /// <remarks>
    /// What a delete and a restore use: both are changes to the row that the
    /// shared conventions of PERSONAL-E3 make, and the version has to follow
    /// them or the next guarded write would be holding a value nobody can have
    /// read.
    /// </remarks>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
