using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// The one place a password becomes the owner's password.
/// </summary>
/// <remarks>
/// Both ways of changing one go through here — the ordinary change in the
/// browser and the recovery on the server — so that they cannot drift into
/// disagreeing about what a password change does. What it does is: check the
/// rule, hash it, write it, and sign out every browser but the one that asked.
/// </remarks>
public sealed class SetTheOwnersPassword(
    IOwners owners, IPasswordHasher passwords, IBrowserSessions sessions, TimeProvider clock)
{
    /// <param name="keep">
    /// The session doing the asking, which stays. Recovery keeps none: nobody
    /// is signed in on the machine the operator is standing at.
    /// </param>
    /// <exception cref="Refusal">The password is missing, too short or too long.</exception>
    public async Task ExecuteAsync(
        Owner owner, string? password, Guid? keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var checkedPassword = Password.Checked(password);
        var now = clock.GetUtcNow();

        owner.ChangePassword(await passwords.HashAsync(checkedPassword, cancellationToken), now);
        await owners.SaveAsync(cancellationToken);

        // A password is usually changed because somebody thinks it is known.
        // Leaving the sessions it opened standing would make the change a
        // gesture.
        await sessions.RevokeAllAsync(owner.Id, keep, now, cancellationToken);
    }
}
