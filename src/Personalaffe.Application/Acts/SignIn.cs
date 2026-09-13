using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// The owner, an email address and a password, and the browser session that
/// comes back when the three agree.
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
    /// The session and its secret, or <c>null</c> — which is every way of being
    /// wrong, told apart nowhere.
    /// </summary>
    public async Task<(BrowserSession Session, string Secret)?> ExecuteAsync(
        string? email, string? password, string? description, CancellationToken cancellationToken)
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
            return null;
        }

        var begun = BrowserSession.Begin(owner.Id, description, clock.GetUtcNow());
        await sessions.AddAsync(begun.Session, cancellationToken);

        return begun;
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
