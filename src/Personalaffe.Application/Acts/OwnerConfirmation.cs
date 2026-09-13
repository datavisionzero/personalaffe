using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// The password, asked for again, in front of every change to how the owner
/// gets in.
/// </summary>
/// <remarks>
/// <para>
/// A signed-in browser left open is the case this is for. Turning the second
/// factor off, taking a fresh set of recovery codes or changing the password
/// are each enough to take the instance over, and none of them should be one
/// click away from a screen somebody walked away from.
/// </para>
/// <para>
/// It refuses with <c>forbidden</c> rather than <c>unauthenticated</c>: the
/// caller is signed in and stays signed in, and a client that treated this as a
/// dead session would sign them out over a typo.
/// </para>
/// </remarks>
public sealed class OwnerConfirmation(ICallerIdentity caller, IOwners owners, IPasswordHasher passwords)
{
    /// <exception cref="Refusal">
    /// The caller is not the owner (<c>forbidden</c>), or the password is not
    /// theirs (<c>forbidden</c>).
    /// </exception>
    public async Task<Owner> ConfirmedAsync(string? password, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("change how this instance is signed in to");

        var owner = await owners.FindAsync(cancellationToken)
            ?? throw new InvalidOperationException("A caller was admitted against an instance with no owner.");

        return await passwords.VerifyAsync(owner.PasswordHash, password ?? string.Empty, cancellationToken)
            ? owner
            : throw Refusal.Forbidden("That is not the current password.");
    }
}
