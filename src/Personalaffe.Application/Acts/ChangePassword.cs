using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// A new password, on the strength of the old one, and every other browser
/// signed out.
/// </summary>
/// <remarks>
/// Changing a password is usually done because somebody thinks it is known.
/// Leaving the sessions it opened standing would make the change a gesture: the
/// point of it is that whoever else was in is now out.
/// </remarks>
public sealed class ChangePassword(
    OwnerConfirmation confirmation, SetTheOwnersPassword setPassword, ICallerIdentity caller)
{
    /// <exception cref="Refusal">
    /// The current password is not the owner's (<c>forbidden</c>), or the new
    /// one is not acceptable (<c>validation</c>).
    /// </exception>
    public async Task ExecuteAsync(
        string? currentPassword, string? password, CancellationToken cancellationToken)
    {
        var owner = await confirmation.ConfirmedAsync(currentPassword, cancellationToken);

        // The same act the recovery on the server goes through, so that the two
        // cannot drift into disagreeing about what a password change does.
        await setPassword.ExecuteAsync(owner, password, caller.Caller.SessionId, cancellationToken);
    }
}
