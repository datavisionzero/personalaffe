using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// Ending the session this request came in on.
/// </summary>
/// <remarks>
/// A caller holding a token has no session to end and is told so rather than
/// quietly succeeding: a token is revoked where it was issued, and a sign-out
/// that answered <c>204</c> to one would be telling a client something was
/// taken away that is still working.
/// </remarks>
public sealed class SignOut(ICallerIdentity caller, IBrowserSessions sessions, TimeProvider clock)
{
    /// <exception cref="Refusal">The caller did not come in on a browser session.</exception>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var who = caller.Caller;

        if (who.SessionId is not { } session)
        {
            throw Refusal.Forbidden(
                "This request did not come in on a browser session, so there is none to end. "
                + "A token is revoked where it was issued.");
        }

        await sessions.RevokeAsync(session, who.Id, clock.GetUtcNow(), cancellationToken);
    }
}
