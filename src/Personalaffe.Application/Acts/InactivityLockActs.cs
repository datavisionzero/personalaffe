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
    IBrowserSessions sessions,
    ICallerIdentity caller,
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
        var currentSession = caller.Caller.SessionId
            ?? throw Refusal.Forbidden("The inactivity lock can only be changed from a browser session.");

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
            await sessions.ApplyInactivityConfigurationAsync(
                owner.Id,
                owner.InactivityLockVersion,
                clock.GetUtcNow(),
                InactivityConfigurationEffect.Disabled,
                currentSession,
                cancellationToken);
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

        var wasEnabled = owner.InactivityLockEnabled;
        var now = clock.GetUtcNow();

        owner.ConfigureInactivityLock(pinHash, minutes, now);
        await owners.SaveAsync(cancellationToken);

        var effect = !wasEnabled
            ? InactivityConfigurationEffect.Activated
            : pinHash is not null
                ? InactivityConfigurationEffect.PinChanged
                : InactivityConfigurationEffect.DurationChanged;

        await sessions.ApplyInactivityConfigurationAsync(
            owner.Id,
            owner.InactivityLockVersion,
            now,
            effect,
            currentSession,
            cancellationToken);
    }
}

public sealed class ReadInactivityLockStatus(ICallerIdentity caller)
{
    public InactivityLockState Execute()
    {
        var who = caller.Caller;
        who.RequireOwner("read the browser inactivity lock");

        return who.SessionId is not null && who.InactivityLock is { } state
            ? state
            : throw Refusal.Forbidden("The inactivity lock belongs to browser sessions, not agent access.");
    }
}

public sealed class RecordInactivityLockActivity(
    ICallerIdentity caller,
    IOwners owners,
    IBrowserSessions sessions,
    TimeProvider clock)
{
    public async Task<InactivityLockState> ExecuteAsync(CancellationToken cancellationToken)
    {
        var who = caller.Caller;
        who.RequireOwner("report browser activity");
        var sessionId = who.SessionId
            ?? throw Refusal.Forbidden("Only a browser session can report browser activity.");
        var now = clock.GetUtcNow();
        var owner = await owners.FindAsync(cancellationToken)
            ?? throw new InvalidOperationException("A caller was admitted against an instance with no owner.");
        var session = await sessions.FindAsync(sessionId, owner.Id, now, cancellationToken)
            ?? throw new Refusal(RefusalCode.Unauthenticated, "This browser session is no longer valid.");

        if (!session.RecordInteraction(owner, now))
        {
            if (session.MarkInactivityLocked(now))
            {
                await sessions.SaveAsync(cancellationToken);
            }

            throw Refusal.Locked("This browser session is locked after inactivity.");
        }

        await sessions.SaveAsync(cancellationToken);
        return session.InactivityState(owner, now);
    }
}

public sealed class UnlockInactivityLock(
    ICallerIdentity caller,
    IOwners owners,
    IBrowserSessions sessions,
    IPasswordHasher passwords,
    TimeProvider clock)
{
    public async Task ExecuteAsync(
        string? pin,
        string? password,
        CancellationToken cancellationToken)
    {
        var who = caller.Caller;
        who.RequireOwner("unlock a browser session");
        var sessionId = who.SessionId
            ?? throw Refusal.Forbidden("Only a browser session can be unlocked.");

        if ((pin is null) == (password is null))
        {
            throw Refusal.Validation(
                new Dictionary<string, string[]>
                {
                    ["pin"] = ["Send either a PIN or the current password."],
                    ["password"] = ["Send either a PIN or the current password."],
                });
        }

        var credential = pin is not null ? UnlockCredential.Pin : UnlockCredential.Password;
        var now = clock.GetUtcNow();
        await using var change = await owners.BeginChangeAsync(cancellationToken);
        var owner = await owners.FindForUpdateAsync(cancellationToken)
            ?? throw new InvalidOperationException("A caller was admitted against an instance with no owner.");
        var session = await sessions.FindAsync(sessionId, owner.Id, now, cancellationToken)
            ?? throw new Refusal(RefusalCode.Unauthenticated, "This browser session is no longer valid.");

        if (!owner.InactivityLockEnabled)
        {
            return;
        }

        if (owner.UnlockBlockedUntil(credential, now) is { } retryAt)
        {
            throw Refusal.Throttled(
                "Too many recent unlock attempts used this proof. Wait before trying it again.",
                retryAt,
                now);
        }

        var correct = credential == UnlockCredential.Pin
            ? await passwords.VerifyAsync(owner.InactivityLockPinHash!, pin!, cancellationToken)
            : await passwords.VerifyAsync(owner.PasswordHash, password!, cancellationToken);

        if (!correct)
        {
            var blockedUntil = owner.RecordFailedUnlock(credential, now);
            await owners.SaveAsync(cancellationToken);
            await change.CompleteAsync(cancellationToken);

            throw Refusal.Throttled(
                "That proof did not unlock this browser. Wait before trying it again.",
                blockedUntil,
                now);
        }

        owner.RecordSuccessfulUnlock(credential);
        session.Unlock(owner.InactivityLockVersion, now);
        await sessions.SaveAsync(cancellationToken);
        await change.CompleteAsync(cancellationToken);
    }
}
