using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>What recovery changed, so that the operator can be told and the owner can see.</summary>
public sealed record Recovered(string Email, bool SecondFactorWasOn, DateTimeOffset At);

/// <summary>
/// The last resort: an owner who has lost the password, the authenticator and
/// the recovery codes gets back in through the machine the instance runs on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is not reachable over HTTP and never will be.</strong> There is
/// no endpoint, no permission and no token that invokes it — it is a verb on the
/// binary that already has the connection string, run by whoever has the host.
/// That is the whole of its authorization, and it is enough: whoever has the
/// machine has the database.
/// </para>
/// <para>
/// It sets a password through the same act the browser's change goes through,
/// turns the second factor off, takes the recovery codes with it, and signs out
/// every browser. Anything less would leave a locked-out owner still locked out
/// — a new password is no use behind an authenticator that is in a river.
/// </para>
/// <para>
/// Nothing else about the instance changes. Agent access is untouched, and so is
/// every byte of content: this is a way back in, not a reset.
/// </para>
/// </remarks>
public sealed class RecoverTheOwner(
    IOwners owners,
    IRecoveryCodes codes,
    SetTheOwnersPassword setPassword,
    TimeProvider clock)
{
    /// <exception cref="Refusal">
    /// The instance has no owner (<c>not-found</c>), or the password is not
    /// acceptable (<c>validation</c>).
    /// </exception>
    public async Task<Recovered> ExecuteAsync(string? password, CancellationToken cancellationToken)
    {
        var owner = await owners.FindAsync(cancellationToken)
            ?? throw Refusal.NotFound(
                "This instance has no owner to recover. Claim it first, through the browser.");

        var wasOn = owner.SecondFactorEnabled;
        var now = clock.GetUtcNow();

        // The password first: if it is refused, nothing else has happened.
        await setPassword.ExecuteAsync(owner, password, keep: null, cancellationToken);

        owner.DisableSecondFactor(now);
        owner.RecordRecovery(now);
        await owners.SaveAsync(cancellationToken);

        await codes.ClearAsync(owner.Id, cancellationToken);

        return new Recovered(owner.Email, wasOn, now);
    }
}
