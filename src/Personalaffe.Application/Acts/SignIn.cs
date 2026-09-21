using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>What happened at the door.</summary>
public enum SignInOutcome
{
    /// <summary>Wrong, in some way that is deliberately not said.</summary>
    Wrong,

    /// <summary>The password was right and an authenticator's code is still wanted.</summary>
    SecondFactorRequired,

    /// <summary>In.</summary>
    SignedIn,
}

/// <summary>What sign-in answers: the outcome, and a session when there is one.</summary>
public sealed record SignedIn(SignInOutcome Outcome, BrowserSession? Session = null, string? Secret = null);

/// <summary>
/// The owner, an email address and a password — and, when they have enrolled an
/// authenticator, a code from it — and the browser session that comes back when
/// they all agree.
/// </summary>
/// <remarks>
/// <para>
/// One answer for every way of being wrong. An instance that has no owner, an
/// address that is not the owner's and a password that is not theirs are the
/// same <c>null</c> here and the same <c>unauthenticated</c> on the wire: an
/// answer that distinguished them would say whether an address is the owner's,
/// to anyone who can reach the port.
/// </para>
/// <para>
/// The password is verified even when there is nobody to verify it against.
/// Without that, "no owner" and "wrong password" differ by the sixty-odd
/// milliseconds an Argon2id takes, and a stopwatch is all it costs to tell
/// them apart.
/// </para>
/// </remarks>
public sealed class SignIn(
    IOwners owners,
    IBrowserSessions sessions,
    IRecoveryCodes recoveryCodes,
    IPasswordHasher passwords,
    TimeProvider clock)
{
    /// <summary>
    /// Something that is not a password, hashed once at startup so that a
    /// sign-in against an instance with no owner costs what a real one costs.
    /// </summary>
    private const string NobodysHash =
        "$argon2id$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA==$"
        + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>
    /// The outcome, and a session when there is one. Every way of being wrong
    /// is one outcome, told apart nowhere.
    /// </summary>
    public async Task<SignedIn> ExecuteAsync(
        string? email,
        string? password,
        string? secondFactor,
        string? description,
        CancellationToken cancellationToken)
    {
        var owner = await owners.FindAsync(cancellationToken);

        // Not Password.Checked: the rules of a new password are not a hint
        // about an existing one, and a sign-in says the same thing to every
        // wrong attempt.
        var presented = password ?? string.Empty;

        var addressMatches = owner is not null && TheOwners(email, owner);

        var correct = await passwords.VerifyAsync(
            owner?.PasswordHash ?? NobodysHash, presented, cancellationToken);

        if (owner is null || !addressMatches || !correct)
        {
            return new SignedIn(SignInOutcome.Wrong);
        }

        if (owner.SecondFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(secondFactor))
            {
                // Saying this out loud tells the caller nothing they did not
                // already prove: they have the password. What it saves is a
                // client that cannot tell "wrong password" from "now the code".
                return new SignedIn(SignInOutcome.SecondFactorRequired);
            }

            if (!await SecondFactorAcceptedAsync(owner, secondFactor, cancellationToken))
            {
                return new SignedIn(SignInOutcome.Wrong);
            }
        }

        var begun = BrowserSession.Begin(
            owner.Id, description, clock.GetUtcNow(), owner.InactivityLockVersion);
        await sessions.AddAsync(begun.Session, cancellationToken);

        return new SignedIn(SignInOutcome.SignedIn, begun.Session, begun.Secret);
    }

    /// <summary>
    /// A code from the authenticator, or one of the codes on the owner's piece
    /// of paper. One field takes either: both answer the same question, and
    /// which one somebody has to hand is not the instance's business.
    /// </summary>
    private async Task<bool> SecondFactorAcceptedAsync(
        Owner owner, string presented, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (Totp.Verify(owner.TotpSecret, presented, now, owner.LastTotpStep, out var step))
        {
            // The step is remembered, so that a code seen over a shoulder is
            // not good for the rest of its thirty seconds.
            owner.RecordSecondFactorStep(step);
            await owners.SaveAsync(cancellationToken);

            return true;
        }

        return await recoveryCodes.ConsumeAsync(
            owner.Id, RecoveryCode.Hash(presented), now, cancellationToken);
    }

    private static bool TheOwners(string? email, Owner owner)
    {
        try
        {
            return Owner.NormalizeEmailForComparison(email) == owner.NormalizedEmail;
        }
        catch (Refusal)
        {
            // Not an address at all is not the owner's address, and is not a
            // separate answer either.
            return false;
        }
    }
}
