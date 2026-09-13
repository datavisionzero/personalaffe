using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>How this instance is signed in to, as much of it as is worth saying.</summary>
public sealed record SecurityState(
    bool SecondFactorEnabled,
    DateTimeOffset? EnrolledAt,
    int RecoveryCodesRemaining,
    DateTimeOffset? RecoveredAt);

/// <summary>What an enrolment offers: the secret, and the URI a phone reads it from.</summary>
public sealed record SecondFactorOffer(string Secret, string Uri);

/// <summary>Whether a second factor stands between the password and the workspace.</summary>
public sealed class ReadSecurity(ICallerIdentity caller, IOwners owners, IRecoveryCodes codes)
{
    public async Task<SecurityState> ExecuteAsync(CancellationToken cancellationToken)
    {
        var owner = await owners.FindAsync(cancellationToken)
            ?? throw new InvalidOperationException("A caller was admitted against an instance with no owner.");

        caller.Caller.RequireOwner("read how this instance is signed in to");

        return new SecurityState(
            owner.SecondFactorEnabled,
            owner.TotpEnrolledAt,
            await codes.RemainingAsync(owner.Id, cancellationToken),
            // A recovery nobody performed is a recovery somebody else
            // performed, so the owner is shown when the last one was.
            owner.RecoveredAt);
    }
}

/// <summary>
/// The first half of enrolment: a secret is offered, and nothing is in force
/// yet.
/// </summary>
/// <remarks>
/// Two halves on purpose. A secret that took effect the moment it was shown
/// would lock the owner out of their own instance on the day they mistyped it
/// into the app, or scanned it with a phone whose clock was wrong — and the way
/// back would be the procedure on the server, for a mistake made in ten
/// seconds.
/// </remarks>
public sealed class BeginSecondFactorEnrolment(
    OwnerConfirmation confirmation, IOwners owners, TimeProvider clock)
{
    public async Task<SecondFactorOffer> ExecuteAsync(string? password, CancellationToken cancellationToken)
    {
        var owner = await confirmation.ConfirmedAsync(password, cancellationToken);

        if (owner.SecondFactorEnabled)
        {
            throw Refusal.Conflict(
                "The second factor is already on. Turn it off before enrolling another authenticator.");
        }

        var secret = Totp.GenerateSecret();

        owner.OfferSecondFactor(secret, clock.GetUtcNow());
        await owners.SaveAsync(cancellationToken);

        return new SecondFactorOffer(secret, Totp.UriFor(secret, owner.Email));
    }
}

/// <summary>
/// The second half: a code made from the offered secret, and the recovery codes
/// that come with turning it on.
/// </summary>
public sealed class ConfirmSecondFactorEnrolment(
    ICallerIdentity caller, IOwners owners, IRecoveryCodes codes, TimeProvider clock)
{
    /// <exception cref="Refusal">
    /// There is no offer or it has gone stale (<c>conflict</c>), or the code is
    /// not one of that secret's (<c>validation</c>).
    /// </exception>
    public async Task<IReadOnlyList<string>> ExecuteAsync(string? code, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("change how this instance is signed in to");

        var owner = await owners.FindAsync(cancellationToken)
            ?? throw new InvalidOperationException("A caller was admitted against an instance with no owner.");

        var now = clock.GetUtcNow();

        if (!Totp.Verify(owner.PendingTotpSecret, code, now, after: null, out var step))
        {
            // A wrong code leaves the instance exactly as it was: the offer
            // stands, and the owner tries again with the next one.
            throw Refusal.Validation(
                "code", "That is not a code of the authenticator that was just enrolled.");
        }

        owner.ConfirmSecondFactor(step, now);
        await owners.SaveAsync(cancellationToken);

        var (issued, text) = RecoveryCode.Issue(owner.Id, now);
        await codes.ReplaceAsync(owner.Id, issued, cancellationToken);

        return text;
    }
}

/// <summary>
/// Turning the second factor off, which needs the password and takes the
/// recovery codes with it.
/// </summary>
public sealed class DisableSecondFactor(
    OwnerConfirmation confirmation,
    IOwners owners,
    IRecoveryCodes codes,
    IBrowserSessions sessions,
    ICallerIdentity caller,
    TimeProvider clock)
{
    public async Task ExecuteAsync(string? password, CancellationToken cancellationToken)
    {
        var owner = await confirmation.ConfirmedAsync(password, cancellationToken);
        var now = clock.GetUtcNow();

        owner.DisableSecondFactor(now);
        await owners.SaveAsync(cancellationToken);

        // Codes for a factor that is off are codes for nothing, and leaving
        // them would mean re-enrolling later silently inherits a sheet of paper
        // from before.
        await codes.ClearAsync(owner.Id, cancellationToken);

        // Whoever was signed in elsewhere was signed in under the old
        // arrangement. This is a security change, and it takes effect
        // everywhere.
        await sessions.RevokeAllAsync(owner.Id, caller.Caller.SessionId, now, cancellationToken);
    }
}

/// <summary>
/// A fresh set of recovery codes, which is also how the old set is thrown away.
/// </summary>
public sealed class ReissueRecoveryCodes(
    OwnerConfirmation confirmation, IRecoveryCodes codes, TimeProvider clock)
{
    public async Task<IReadOnlyList<string>> ExecuteAsync(
        string? password, CancellationToken cancellationToken)
    {
        var owner = await confirmation.ConfirmedAsync(password, cancellationToken);

        if (!owner.SecondFactorEnabled)
        {
            throw Refusal.Conflict(
                "There is no second factor, so there is nothing to recover past. "
                + "Recovery codes come with enrolling an authenticator.");
        }

        var (issued, text) = RecoveryCode.Issue(owner.Id, clock.GetUtcNow());
        await codes.ReplaceAsync(owner.Id, issued, cancellationToken);

        return text;
    }
}
