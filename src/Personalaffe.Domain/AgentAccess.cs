namespace Personalaffe.Domain;

/// <summary>
/// A named, revocable authorization for an agent acting on the owner's behalf,
/// with no access, read access or read/write access to each application
/// (<c>CONTEXT.md</c>, Agent access).
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is not a second human account and never becomes one.</strong>
/// There is no password, no session and no way to sign in as one; what it has
/// is a token, and what the token reaches is the applications the owner
/// granted. Issuing credentials, changing how the owner signs in, and every
/// other owner-only act are outside it — not by a permission that could be
/// granted, but because no permission for them exists.
/// </para>
/// <para>
/// Revoking is a timestamp and not a deletion. A revoked access still names the
/// agent everywhere it ever acted, and simply fails at the door.
/// </para>
/// <para>
/// The token lives on this row rather than in a table of its own: an agent
/// access has exactly one token that works, and reissuing replaces it. What is
/// kept is the digest and a recognisable head — the secret itself is shown once
/// and is afterwards nowhere.
/// </para>
/// </remarks>
public sealed class AgentAccess
{
    public const int NameMaxLength = 100;

    private AgentAccess()
    {
        // EF Core materializes through this; every other route goes through Grant.
    }

    private AgentAccess(string name, Permissions permissions, string secret, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7(at);
        Name = name;
        Permissions = permissions;
        TokenPrefix = TokenSecret.PrefixOf(secret);
        TokenHash = TokenSecret.HashOf(secret);
        TokenIssuedAt = at;
        CreatedAt = at;
        UpdatedAt = at;
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// What the owner calls it, so that a list of them is a list of answers to
    /// "what is this for". Unique, case-insensitively: two agents called the
    /// same thing are two things nobody can tell apart at the moment of
    /// revoking one.
    /// </summary>
    public string Name { get; private set; } = null!;

    public Permissions Permissions { get; private set; } = Permissions.None;

    /// <summary>The head of the current token, which is all a list shows.</summary>
    public string TokenPrefix { get; private set; } = null!;

    public byte[] TokenHash { get; private set; } = null!;

    public DateTimeOffset TokenIssuedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// When this token was last used, kept roughly rather than exactly: it is
    /// written at most once every <see cref="UseInterval"/>, because otherwise
    /// every read an agent makes would also be a write.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool Revoked => RevokedAt is not null;

    /// <summary>How often "it was used" is written down.</summary>
    public static readonly TimeSpan UseInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A new access and the secret that reaches it. The secret is returned once,
    /// to be shown once, and is afterwards recoverable from nothing.
    /// </summary>
    /// <exception cref="Refusal">The name is missing or over its limit.</exception>
    public static (AgentAccess Access, string Secret) Grant(
        string? name, Permissions permissions, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var secret = TokenSecret.Generate();

        return (new AgentAccess(NormalizeName(name), permissions, secret, at), secret);
    }

    /// <exception cref="Refusal">The name is missing or over its limit.</exception>
    public void Rename(string? name, DateTimeOffset at)
    {
        Name = NormalizeName(name);
        UpdatedAt = at;
    }

    public void Grant(Permissions permissions, DateTimeOffset at)
    {
        Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        UpdatedAt = at;
    }

    /// <summary>
    /// A new token, which is also how the old one stops working. There is one
    /// token that works, and this is it.
    /// </summary>
    public string ReissueToken(DateTimeOffset at)
    {
        var secret = TokenSecret.Generate();

        TokenPrefix = TokenSecret.PrefixOf(secret);
        TokenHash = TokenSecret.HashOf(secret);
        TokenIssuedAt = at;
        LastUsedAt = null;
        UpdatedAt = at;

        return secret;
    }

    /// <summary>Immediate, and revoking a revoked access changes nothing.</summary>
    public void Revoke(DateTimeOffset at)
    {
        if (RevokedAt is null)
        {
            RevokedAt = at;
            UpdatedAt = at;
        }
    }

    /// <summary>
    /// Records that the token was used, at most once every
    /// <see cref="UseInterval"/>. <c>true</c> when the row changed.
    /// </summary>
    public bool Used(DateTimeOffset at)
    {
        if (Revoked || (LastUsedAt is { } last && at - last < UseInterval))
        {
            return false;
        }

        LastUsedAt = at;
        return true;
    }

    /// <summary>The name as it would be stored.</summary>
    /// <exception cref="Refusal">It is missing or over its limit.</exception>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw Refusal.Validation("name", "Agent access has a name.");
        }

        return trimmed.Length > NameMaxLength
            ? throw Refusal.Validation("name", $"A name is at most {NameMaxLength} characters.")
            : trimmed;
    }
}
