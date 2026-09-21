using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// Configures the optional lock that stands between an inactive browser and
/// the workspace. The password confirms every change; the PIN is hashed by the
/// same deliberately slow port as the password and never leaves this act.
/// </summary>
public sealed class ConfigureInactivityLock(
    OwnerConfirmation confirmation,
    IOwners owners,
    IPasswordHasher passwords,
    TimeProvider clock)
{
    public async Task ExecuteAsync(
        bool? enabled,
        string? pin,
        int? inactivityMinutes,
        string? currentPassword,
        CancellationToken cancellationToken)
    {
        var owner = await confirmation.ConfirmedAsync(currentPassword, cancellationToken);

        if (enabled is null)
        {
            throw Refusal.Validation("enabled", "Whether the inactivity lock is enabled is required.");
        }

        if (!enabled.Value)
        {
            if (pin is not null)
            {
                throw Refusal.Validation("pin", "A PIN is not accepted while the inactivity lock is off.");
            }

            owner.DisableInactivityLock(clock.GetUtcNow());
            await owners.SaveAsync(cancellationToken);
            return;
        }

        if (inactivityMinutes is null)
        {
            throw Refusal.Validation(
                "inactivity_minutes", "Inactivity in whole minutes is required when the lock is on.");
        }

        var minutes = Owner.ValidateInactivityLockMinutes(inactivityMinutes.Value);
        string? pinHash = null;

        if (pin is not null)
        {
            var acceptedPin = Owner.ValidateInactivityLockPin(pin);
            pinHash = await passwords.HashAcceptedSecretAsync(acceptedPin, cancellationToken);
        }
        else if (!owner.InactivityLockEnabled)
        {
            throw Refusal.Validation("pin", "A PIN is required when the inactivity lock is turned on.");
        }

        owner.ConfigureInactivityLock(pinHash, minutes, clock.GetUtcNow());
        await owners.SaveAsync(cancellationToken);
    }
}
