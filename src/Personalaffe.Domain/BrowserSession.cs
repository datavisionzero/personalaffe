using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Personalaffe.Domain;

/// <summary>
/// A signed-in browser: server-side, individually revocable, and known to the
/// browser only by a secret in a cookie.
/// </summary>
/// <remarks>
/// <para>
/// Server-side rather than a signed token, because "revoked" has to mean
/// revoked. A self-contained token cannot be taken back before it expires
/// without a list of the ones that were — which is this table, without the
/// token.
/// </para>
/// <para>
/// Two lifetimes, and they answer different questions. The idle one ends a
/// session nobody has used: a browser left open on a machine somebody else now
/// uses stops being a way in. The absolute one ends every session eventually,
/// however busy — a secret that never expires is a secret that is eventually
/// somewhere it should not be.
/// </para>
/// <para>
/// <see cref="Touch"/> is rate-limited on purpose. Writing a row on every
/// request would make a read of the workspace a write to the database, and the
/// thing being kept up to date is measured in days.
/// </para>
/// </remarks>
public sealed class BrowserSession
{
    /// <summary>How long a session survives without being used.</summary>
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(7);

    /// <summary>How long a session survives at all, however busy.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);

    /// <summary>How often being used is written down.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    /// <summary>The length of a SHA-256 digest.</summary>
    public const int SecretHashLength = 32;

    /// <summary>Enough of the browser's own description to recognise a session by.</summary>
    public const int DescriptionMaxLength = 200;

    private BrowserSession()
    {
        // EF Core materializes through this; every other route goes through Begin.
    }

    private BrowserSession(Guid ownerId, byte[] secretHash, string? description, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        OwnerId = ownerId;
        SecretHash = secretHash;
        Description = description;
        CreatedAt = now;
        LastUsedAt = now;
        ExpiresAt = now.Add(AbsoluteLifetime);
    }

    public Guid Id { get; private init; }

    public Guid OwnerId { get; private init; }

    /// <summary>The SHA-256 of the secret. The secret itself is kept nowhere.</summary>
    public byte[] SecretHash { get; private init; } = null!;

    /// <summary>
    /// What the browser called itself when the session began, so that a person
    /// looking at the list can tell which one is the laptop and which one is
    /// the phone. Nothing is derived from it and nothing is trusted about it.
    /// </summary>
    public string? Description { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private init; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool Revoked => RevokedAt is not null;

    /// <summary>
    /// A new session and the secret that reaches it. The secret is returned
    /// once, to be put in a cookie, and is never recoverable from the row.
    /// </summary>
    public static (BrowserSession Session, string Secret) Begin(
        Guid ownerId, string? description, DateTimeOffset now)
    {
        // 256 bits, generated rather than chosen, which is why a plain digest
        // is the right thing to store it as: there is nothing here for a slow
        // hash to protect, and this is looked up on every request.
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        return (new BrowserSession(ownerId, Hash(secret), Shortened(description), now), secret);
    }

    /// <summary>What a presented secret is looked up by.</summary>
    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>Whether this session still admits anybody.</summary>
    public bool IsValid(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now && LastUsedAt.Add(IdleLifetime) > now;

    /// <summary>
    /// Records that the session was used, at most once every
    /// <see cref="TouchInterval"/>. <c>true</c> when the row changed and has to
    /// be written.
    /// </summary>
    public bool Touch(DateTimeOffset now)
    {
        if (!IsValid(now) || now - LastUsedAt < TouchInterval)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }

    /// <summary>Immediate, and revoking a revoked session changes nothing.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private static string? Shortened(string? description)
    {
        var trimmed = description?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length > DescriptionMaxLength ? trimmed[..DescriptionMaxLength] : trimmed;
    }
}
