namespace Personalaffe.Domain;

/// <summary>
/// Who did something, kept as it read at the time.
/// </summary>
/// <remarks>
/// <para>
/// It is a copy and not a reference on purpose. Agent access is revocable, and
/// a Trash entry that says "deleted by an access that no longer exists" is a
/// Trash entry the owner cannot reason about — the whole question they are
/// asking of that list is which of their agents did this. So the name travels
/// with the deletion, and nothing links back to a row that can be revoked, made
/// again under the same name, or given to something else.
/// </para>
/// <para>
/// The owner has no name here, the way <see cref="Caller"/> gives them none:
/// there is one of them, and "the owner" is the whole answer.
/// </para>
/// </remarks>
public sealed record Actor
{
    public required CallerKind Kind { get; init; }

    /// <summary>The owner's id, or the agent access's, as it was.</summary>
    public required Guid Id { get; init; }

    /// <summary>What the owner called the agent access. Null for the owner.</summary>
    public string? Name { get; init; }

    /// <summary>Whoever is making this request, snapshotted.</summary>
    public static Actor Of(Caller caller) => new()
    {
        Kind = (caller ?? throw new ArgumentNullException(nameof(caller))).Kind,
        Id = caller.Id,
        Name = caller.Name,
    };

    /// <summary>How a sentence names them: "the owner", or "the agent access X".</summary>
    public string Describe() =>
        Kind == CallerKind.Owner ? "the owner" : $"the agent access `{Name}`";
}
