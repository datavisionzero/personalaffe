using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// What a presented credential admits: the caller behind it, or nobody.
/// </summary>
/// <remarks>
/// <para>
/// The first thing every operation behind the door does, and the only place any
/// of them learns who is calling. A browser session arrives as a cookie and a
/// token as <c>Authorization: Bearer</c>; both are hashed and looked up, and
/// neither is ever compared as text — a miss and a hit cost one index lookup
/// each, so there is no timing to equalise.
/// </para>
/// <para>
/// Nothing here tells a credential that never existed from one that was
/// revoked, and nothing it returns says which it was. Both are
/// <c>unauthenticated</c>.
/// </para>
/// </remarks>
public sealed class AuthenticateCaller(
    IBrowserSessions sessions, IAgentAccessStore agents, TimeProvider clock)
{
    private const string Scheme = "Bearer";

    /// <summary>The caller behind a session cookie's secret, or <c>null</c>.</summary>
    public async Task<Caller?> FromSessionAsync(string? secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var session = await sessions.AdmitAsync(
            BrowserSession.Hash(secret), clock.GetUtcNow(), cancellationToken);

        return session is null ? null : Caller.Owner(session.OwnerId, session.Id);
    }

    /// <summary>
    /// The caller behind an <c>Authorization</c> header, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// A header that is not a bearer token of this instance's shape never
    /// becomes a query: the wrong scheme, nothing after it, or a length no
    /// token has is refused here.
    /// </remarks>
    public async Task<Caller?> FromTokenAsync(string? authorization, CancellationToken cancellationToken)
    {
        if (!TryReadSecret(authorization, out var secret))
        {
            return null;
        }

        var access = await agents.AdmitAsync(
            TokenSecret.HashOf(secret), clock.GetUtcNow(), cancellationToken);

        // Revoked and never-issued are one answer, and nothing says which.
        return access is null || access.Revoked ? null : Caller.Agent(access);
    }

    private static bool TryReadSecret(string? authorization, out string secret)
    {
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        var parts = authorization.Trim().Split(
            ' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 2
            || !parts[0].Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !TokenSecret.IsAcceptable(parts[1]))
        {
            return false;
        }

        secret = parts[1];
        return true;
    }
}
