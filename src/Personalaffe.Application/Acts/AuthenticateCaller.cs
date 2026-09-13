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
/// <para>
/// The token half arrives with agent access (PERSONAL-12). Until then a bearer
/// token admits nobody, which is the honest answer: no token has been issued.
/// </para>
/// </remarks>
public sealed class AuthenticateCaller(IBrowserSessions sessions, TimeProvider clock)
{
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
}
