using Personalaffe.Application.Acts;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>What <c>POST /api/session</c> takes.</summary>
/// <param name="SecondFactor">
/// A code from the authenticator, or one of the owner's recovery codes. One
/// field takes either: both answer the same question, and which one somebody
/// has to hand is not the instance's business.
/// </param>
public sealed record SignInRequest(string? Email, string? Password, string? SecondFactor);

/// <summary>One signed-in browser, as <c>GET /api/sessions</c> lists it.</summary>
public sealed record SessionResponse(
    Guid Id,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool Current);

public sealed record InactivityLockStatusResponse(
    bool Enabled,
    bool Locked,
    DateTimeOffset? LocksAt);

public sealed record UnlockInactivityLockRequest(string? Pin, string? Password);

/// <summary>
/// The browser's way in and out (<c>docs/api.md</c>).
/// </summary>
/// <remarks>
/// A session is a row on the server and a secret in a cookie, which is what
/// makes revoking one mean something. The cookie is <c>HttpOnly</c> — no script
/// on the page can read it, so an injected one cannot carry it away — and its
/// strictness follows the request's own scheme (<see cref="BrowserCookie"/>).
/// </remarks>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSession(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/session", async (
                SignInRequest request,
                HttpContext http,
                SignIn act,
                LoginThrottle throttle,
                CancellationToken cancellationToken) =>
            {
                var account = Folded(request.Email);
                var source = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // A throttled attempt answers exactly what a wrong one answers.
                // Saying "you are being throttled" would be telling a guesser
                // that they have found something worth guessing at.
                if (throttle.IsBlocked(account, source))
                {
                    return Wrong();
                }

                var signedIn = await act.ExecuteAsync(
                    request.Email,
                    request.Password,
                    request.SecondFactor,
                    http.Request.Headers.UserAgent.ToString(),
                    cancellationToken);

                if (signedIn.Outcome == SignInOutcome.SecondFactorRequired)
                {
                    // Its own code, because a client has to be able to tell
                    // "that was wrong" from "now the code" — and the caller has
                    // already proved they have the password, so this says
                    // nothing they did not know.
                    return Problems.Result(
                        RefusalCode.SecondFactor,
                        "Send the same request again with `second_factor`: a code from the authenticator, "
                        + "or one of the recovery codes.");
                }

                if (signedIn is not { Outcome: SignInOutcome.SignedIn, Session: { } session, Secret: { } secret })
                {
                    throttle.Failed(account, source);
                    return Wrong();
                }

                throttle.Succeeded(account);

                var cookie = BrowserCookie.For(http.Request);
                http.Response.Cookies.Append(cookie.Name, secret, cookie.Options(session.ExpiresAt));

                return Results.NoContent();
            })
            .AllowAnonymous()
            .WithName("SignIn")
            .WithSummary("Sign in as the owner. The session comes back as a cookie.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapDelete("/session", async (
                HttpContext http, SignOut act, CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(cancellationToken);

                BrowserCookie.Forget(http.Response);

                return Results.NoContent();
            })
            .WithName("SignOut")
            .WithSummary("End the session this request came in on.")
            .WithMetadata(new AllowWhileInactivityLocked())
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapGet("/session/lock", (ReadInactivityLockStatus act) =>
            {
                var state = act.Execute();
                return Results.Ok(new InactivityLockStatusResponse(
                    state.Enabled, state.Locked, state.LocksAt));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithMetadata(new AllowWhileInactivityLocked())
            .WithName("ReadInactivityLockStatus")
            .WithSummary("Whether this browser session is locked after inactivity.")
            .Produces<InactivityLockStatusResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPost("/session/lock/activity", async (
                RecordInactivityLockActivity act,
                CancellationToken cancellationToken) =>
            {
                var state = await act.ExecuteAsync(cancellationToken);
                return Results.Ok(new InactivityLockStatusResponse(
                    state.Enabled, state.Locked, state.LocksAt));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithMetadata(new AllowWhileInactivityLocked())
            .WithName("RecordInactivityLockActivity")
            .WithSummary("Report deliberate activity while this browser session is still unlocked.")
            .Produces<InactivityLockStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status423Locked);

        endpoints.MapPost("/session/lock/unlock", async (
                UnlockInactivityLockRequest request,
                UnlockInactivityLock act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(request.Pin, request.Password, cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithMetadata(new AllowWhileInactivityLocked())
            .WithName("UnlockInactivityLock")
            .WithSummary("Unlock this valid browser session with the PIN or current password.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        endpoints.MapGet("/sessions", async (ListSessions act, CancellationToken cancellationToken) =>
            {
                var live = await act.ExecuteAsync(cancellationToken);

                return Results.Ok(live.Select(session => new SessionResponse(
                    session.Id,
                    session.Description,
                    session.CreatedAt,
                    session.LastUsedAt,
                    session.ExpiresAt,
                    session.Current)));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ListSessions")
            .WithSummary("Where this instance is signed in.")
            .Produces<IReadOnlyList<SessionResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapDelete("/sessions/{id:guid}", async (
                Guid id, RevokeSession act, CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(id, cancellationToken);

                return Results.NoContent();
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("RevokeSession")
            .WithSummary("End one signed-in browser, which may be this one.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapDelete("/sessions", async (
                RevokeOtherSessions act, CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(cancellationToken);

                return Results.NoContent();
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("RevokeOtherSessions")
            .WithSummary("End every signed-in browser but this one.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    /// <summary>
    /// The one answer every way of being wrong gets: no owner, not the owner's
    /// address, not their password, or too many attempts. Distinguishing them
    /// would say whether an address is the owner's, to whoever can reach the
    /// port.
    /// </summary>
    private static IResult Wrong() =>
        Problems.Result(RefusalCode.Unauthenticated, "The email address or the password is not correct.");

    private static string Folded(string? email) =>
        email?.Trim().ToLowerInvariant() ?? string.Empty;
}
