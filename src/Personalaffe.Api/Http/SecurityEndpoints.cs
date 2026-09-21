using Personalaffe.Application.Acts;

namespace Personalaffe.Api.Http;

/// <summary>What <c>GET /api/security</c> answers.</summary>
public sealed record SecurityResponse(
    bool SecondFactorEnabled,
    DateTimeOffset? EnrolledAt,
    int RecoveryCodesRemaining,
    DateTimeOffset? RecoveredAt,
    bool InactivityLockEnabled,
    int InactivityMinutes);

/// <summary>A change to how this instance is signed in to, with the password that authorizes it.</summary>
public sealed record PasswordRequest(string? Password);

/// <summary>The code that proves the authenticator was enrolled correctly.</summary>
public sealed record CodeRequest(string? Code);

/// <summary>A new password, on the strength of the old one.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? Password);

/// <summary>
/// The complete desired inactivity-lock state. A PIN may be omitted while an
/// enabled lock keeps its existing one; turning it on for the first time needs one.
/// </summary>
public sealed record InactivityLockRequest(
    bool? Enabled,
    string? Pin,
    int? InactivityMinutes,
    string? CurrentPassword);

/// <summary>The secret an authenticator is given, and the URI it is usually read from.</summary>
public sealed record SecondFactorOfferResponse(string Secret, string Uri);

/// <summary>The codes, shown once and never again.</summary>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// How the owner gets in, and the three ways of changing it
/// (<c>docs/api.md</c>).
/// </summary>
/// <remarks>
/// Every change here asks for the password again, whoever is asking and however
/// recently they signed in. A browser left open is enough to take an instance
/// over otherwise, and none of these should be one click away from a screen
/// somebody walked away from.
/// </remarks>
public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapSecurity(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/security", async (ReadSecurity act, CancellationToken cancellationToken) =>
            {
                var state = await act.ExecuteAsync(cancellationToken);

                return Results.Ok(new SecurityResponse(
                    state.SecondFactorEnabled,
                    state.EnrolledAt,
                    state.RecoveryCodesRemaining,
                    state.RecoveredAt,
                    state.InactivityLockEnabled,
                    state.InactivityMinutes));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ReadSecurity")
            .WithSummary("Whether a second factor stands between the password and the workspace.")
            .Produces<SecurityResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPut("/security/inactivity-lock", async (
                InactivityLockRequest request,
                ConfigureInactivityLock act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(
                    request.Enabled,
                    request.Pin,
                    request.InactivityMinutes,
                    request.CurrentPassword,
                    cancellationToken);

                return Results.NoContent();
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ConfigureInactivityLock")
            .WithSummary("Turn the browser inactivity lock on or off, or change its PIN and duration.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPost("/security/second-factor", async (
                PasswordRequest request,
                BeginSecondFactorEnrolment act,
                CancellationToken cancellationToken) =>
            {
                var offer = await act.ExecuteAsync(request.Password, cancellationToken);

                // Nothing is in force yet. The secret takes effect when a code
                // made from it comes back, and not before.
                return Results.Ok(new SecondFactorOfferResponse(offer.Secret, offer.Uri));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("BeginSecondFactorEnrolment")
            .WithSummary("Offer a shared secret for an authenticator. Nothing changes until it is confirmed.")
            .Produces<SecondFactorOfferResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/security/second-factor/confirm", async (
                CodeRequest request,
                ConfirmSecondFactorEnrolment act,
                CancellationToken cancellationToken) =>
            {
                var codes = await act.ExecuteAsync(request.Code, cancellationToken);

                return Results.Ok(new RecoveryCodesResponse(codes));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ConfirmSecondFactorEnrolment")
            .WithSummary("Turn the offered authenticator on, and take the recovery codes that come with it.")
            .Produces<RecoveryCodesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/security/second-factor/off", async (
                PasswordRequest request,
                DisableSecondFactor act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(request.Password, cancellationToken);

                return Results.NoContent();
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("DisableSecondFactor")
            .WithSummary("Turn the second factor off. The recovery codes go with it, and every other browser is signed out.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPost("/security/recovery-codes", async (
                PasswordRequest request,
                ReissueRecoveryCodes act,
                CancellationToken cancellationToken) =>
            {
                var codes = await act.ExecuteAsync(request.Password, cancellationToken);

                return Results.Ok(new RecoveryCodesResponse(codes));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ReissueRecoveryCodes")
            .WithSummary("A fresh set of recovery codes. The old set stops working.")
            .Produces<RecoveryCodesResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/security/password", async (
                ChangePasswordRequest request,
                ChangePassword act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(request.CurrentPassword, request.Password, cancellationToken);

                return Results.NoContent();
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ChangePassword")
            .WithSummary("Change the password. Every other browser is signed out.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
