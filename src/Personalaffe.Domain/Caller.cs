namespace Personalaffe.Domain;

/// <summary>The two kinds of thing that can be acting (<c>CONTEXT.md</c>).</summary>
public enum CallerKind
{
    /// <summary>The sole human this instance belongs to.</summary>
    Owner,

    /// <summary>A named, revocable authorization for an agent acting on the owner's behalf.</summary>
    Agent,
}

/// <summary>
/// Whoever the door admitted, as one value the acts take whole.
/// </summary>
/// <remarks>
/// <para>
/// It is a value and not a bag of claims because everything downstream asks the
/// same two questions of it — which kind, and may it do this — and neither is a
/// string to be parsed back out of a principal.
/// </para>
/// <para>
/// <see cref="SessionId"/> is set only when the credential was a browser
/// session. That is what lets signing out revoke the session this request came
/// in on, and what the CSRF guard keys on: a request carrying a bearer token
/// was not sent by a page somebody else's site loaded.
/// </para>
/// </remarks>
public sealed record Caller
{
    private Caller()
    {
    }

    public required CallerKind Kind { get; init; }

    /// <summary>The owner's id, or the agent access's.</summary>
    public required Guid Id { get; init; }

    /// <summary>The browser session this request came in on, where it did.</summary>
    public Guid? SessionId { get; init; }

    /// <summary>What the owner calls this agent access. The owner has no name here.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// What this caller may do, one answer per application. The owner's is
    /// everything; an agent's is what the owner granted.
    /// </summary>
    public required Permissions Permissions { get; init; }

    /// <summary>Explicit visibility context for this request only; never a permission grant.</summary>
    public bool PrivateBookmarks { get; init; }

    public bool IsOwner => Kind == CallerKind.Owner;

    /// <summary>The owner, in a browser.</summary>
    public static Caller Owner(Guid id, Guid? sessionId = null) =>
        new() { Kind = CallerKind.Owner, Id = id, SessionId = sessionId, Permissions = Permissions.Full };

    /// <summary>An agent, with exactly what it was granted.</summary>
    public static Caller Agent(AgentAccess access) =>
        new()
        {
            Kind = CallerKind.Agent,
            Id = (access ?? throw new ArgumentNullException(nameof(access))).Id,
            Name = access.Name,
            Permissions = access.Permissions,
        };

    /// <summary>
    /// The caller, or a refusal, for a read of <paramref name="application"/>.
    /// </summary>
    /// <exception cref="Refusal">This caller has no access to it.</exception>
    public Caller RequireRead(WorkspaceApplication application) =>
        Permissions.MayRead(application)
            ? this
            : throw Refusal.Forbidden($"This access does not reach {Named(application)}.");

    /// <summary>
    /// The caller, or a refusal, for a change to <paramref name="application"/>
    /// — which deletion is, whatever kind of deletion it is.
    /// </summary>
    /// <exception cref="Refusal">This caller may not write it, or may not see it at all.</exception>
    public Caller RequireWrite(WorkspaceApplication application) =>
        Permissions.MayWrite(application)
            ? this
            : throw Refusal.Forbidden(
                Permissions.MayRead(application)
                    ? $"This access reads {Named(application)} and does not change it."
                    : $"This access does not reach {Named(application)}.");

    private static string Named(WorkspaceApplication application) =>
        application.ToString().ToLowerInvariant();

    /// <summary>
    /// The owner, or a refusal. What this guards is everything an agent is
    /// deliberately not able to do: issue a credential, change a security
    /// setting, empty the Trash, reset the instance.
    /// </summary>
    /// <exception cref="Refusal">The caller is not the owner.</exception>
    public Caller RequireOwner(string act) =>
        IsOwner
            ? this
            : throw Refusal.Forbidden(
                $"Only the owner may {act}. Agent access acts on the owner's behalf in the applications "
                + "it was given and nowhere else.");
}
