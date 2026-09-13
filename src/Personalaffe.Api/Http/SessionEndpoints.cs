using Personalaffe.Application.Acts;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>What <c>POST /api/session</c> takes.</summary>
public sealed record SignInRequest(string? Email, string? Password);

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

                var issued = await act.ExecuteAsync(
                    request.Email,
                    request.Password,
                    http.Request.Headers.UserAgent.ToString(),
                    cancellationToken);

                if (issued is not { } session)
                {
                    throttle.Failed(account, source);
                    return Wrong();
                }

                throttle.Succeeded(account);

                var cookie = BrowserCookie.For(http.Request);
                http.Response.Cookies.Append(
                    cookie.Name, session.Secret, cookie.Options(session.Session.ExpiresAt));

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
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
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
