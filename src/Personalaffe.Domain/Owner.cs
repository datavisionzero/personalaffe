namespace Personalaffe.Domain;

/// <summary>
/// The sole human to whom an instance and all of its content belong
/// (<c>CONTEXT.md</c>, Owner).
/// </summary>
/// <remarks>
/// <para>
/// One row, forever. There is no users table, no role and no invitation,
/// because there is no second human account to hold one
/// (<c>docs/codebase.md</c>). What holds the rule is not this class: it is the
/// unique index on <see cref="Singleton"/> in the schema, so that a second
/// owner is impossible even for a caller that never passed through an act.
/// </para>
/// <para>
/// The email address is the login identifier and nothing else. personalaffe
/// sends no mail, has no SMTP setting and needs none: recovery is the recovery
/// codes and, failing those, the procedure an operator runs on the machine
/// itself.
/// </para>
/// <para>
/// The password is here only as the hash of it. What produced the hash is
/// Infrastructure's business and this type never learns it — the value is
/// self-describing and carries its own parameters, so an owner set up under one
/// cost can be verified after that cost has been raised.
/// </para>
/// </remarks>
public sealed class Owner
{
    public const int EmailMaxLength = 100;

    public const int PinMinLength = 4;

    public const int PinMaxLength = 6;

    public const int MinInactivityLockMinutes = 1;

    public const int MaxInactivityLockMinutes = 1440;

    public const int DefaultInactivityLockMinutes = 5;

    public const int UnlockAttemptLimit = 5;

    public static readonly TimeSpan UnlockAttemptWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long an offered second-factor secret can still be confirmed. Long
    /// enough to find the phone, short enough that a secret shown on a screen
    /// somebody walked away from is not an enrolment waiting to happen.
    /// </summary>
    public static readonly TimeSpan EnrolmentOfferLifetime = TimeSpan.FromMinutes(15);

    private Owner()
    {
        // EF Core materializes through this; every other route goes through Claim.
    }

    private Owner(Guid id, string email, string passwordHash, DateTimeOffset at)
    {
        Id = id;
        Email = NormalizeEmail(email);
        NormalizedEmail = NormalizeEmailForComparison(email);
        PasswordHash = passwordHash;
        CreatedAt = at;
        UpdatedAt = at;
    }

    public Guid Id { get; private init; }

    /// <summary>The login identifier, as the owner wrote it.</summary>
    public string Email { get; private set; } = null!;

    /// <summary>The same address folded for comparison: what sign-in looks up by.</summary>
    public string NormalizedEmail { get; private set; } = null!;

    /// <summary>The encoded hash, self-describing and never reversible.</summary>
    public string PasswordHash { get; private set; } = null!;

    /// <summary>
    /// Always <c>true</c>, and unique in the table: the schema's way of saying
    /// that there is one owner. A column rather than a fixed key, because a
    /// fixed key is a value somebody can choose differently and this is a value
    /// nobody can.
    /// </summary>
    public bool Singleton { get; private init; } = true;

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When the owner last changed: the address, the password, or the second factor.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// The confirmed shared secret of the second factor, or nothing. Set only
    /// once a code made from it has been shown to work — an unconfirmed secret
    /// that was already in force is how an owner locks themselves out of their
    /// own instance with a mistyped app.
    /// </summary>
    public string? TotpSecret { get; private set; }

    /// <summary>When the second factor was confirmed.</summary>
    public DateTimeOffset? TotpEnrolledAt { get; private set; }

    /// <summary>An enrolment that has been offered and not yet proven.</summary>
    public string? PendingTotpSecret { get; private set; }

    /// <summary>When it was offered; an offer nobody confirmed goes stale.</summary>
    public DateTimeOffset? PendingTotpSecretAt { get; private set; }

    /// <summary>
    /// The last step a code was accepted for. Anything at or below it is
    /// refused however correct it is, so that a code read over somebody's
    /// shoulder is not good for the rest of its thirty seconds.
    /// </summary>
    public long? LastTotpStep { get; private set; }

    /// <summary>Whether a code is asked for after the password.</summary>
    public bool SecondFactorEnabled => TotpSecret is not null;

    /// <summary>
    /// When somebody last recovered this instance from the machine it runs on.
    /// Kept so that the owner sees it afterwards: a recovery nobody performed
    /// is a recovery somebody else performed.
    /// </summary>
    public DateTimeOffset? RecoveredAt { get; private set; }

    /// <summary>
    /// The slow, salted hash of the optional inactivity PIN. The PIN itself is
    /// never stored. No hash means the additional browser lock is off.
    /// </summary>
    public string? InactivityLockPinHash { get; private set; }

    /// <summary>How many minutes of deliberate inactivity precede the lock.</summary>
    public int InactivityLockMinutes { get; private set; } = DefaultInactivityLockMinutes;

    /// <summary>
    /// Moves with every lock-configuration change so that a browser session
    /// can tell whether an earlier unlock decision still belongs to the
    /// current PIN and duration.
    /// </summary>
    public long InactivityLockVersion { get; private set; }

    public bool InactivityLockEnabled => InactivityLockPinHash is not null;

    public int PinUnlockFailures { get; private set; }

    public DateTimeOffset? PinUnlockWindowStartedAt { get; private set; }

    public DateTimeOffset? PinUnlockBlockedUntil { get; private set; }

    public int PasswordUnlockFailures { get; private set; }

    public DateTimeOffset? PasswordUnlockWindowStartedAt { get; private set; }

    public DateTimeOffset? PasswordUnlockBlockedUntil { get; private set; }

    /// <summary>
    /// The owner of an instance that had none, from an address and a hash that
    /// has already been made.
    /// </summary>
    /// <exception cref="Refusal">The address is not one, or is too long.</exception>
    public static Owner Claim(string email, string passwordHash, DateTimeOffset at) =>
        new(
            Guid.CreateVersion7(at),
            email,
            string.IsNullOrWhiteSpace(passwordHash)
                ? throw new ArgumentException("A password hash is required.", nameof(passwordHash))
                : passwordHash,
            at);

    public void ChangePassword(string passwordHash, DateTimeOffset at)
    {
        PasswordHash = string.IsNullOrWhiteSpace(passwordHash)
            ? throw new ArgumentException("A password hash is required.", nameof(passwordHash))
            : passwordHash;
        UpdatedAt = at;
    }

    /// <summary>Offers a secret for enrolment, replacing any offer not yet confirmed.</summary>
    public void OfferSecondFactor(string secret, DateTimeOffset at)
    {
        PendingTotpSecret = string.IsNullOrWhiteSpace(secret)
            ? throw new ArgumentException("A shared secret is required.", nameof(secret))
            : secret;
        PendingTotpSecretAt = at;
    }

    /// <summary>
    /// Turns the offer into the second factor, on the strength of a code made
    /// from it.
    /// </summary>
    /// <exception cref="Refusal">Nothing has been offered.</exception>
    public void ConfirmSecondFactor(long step, DateTimeOffset at)
    {
        if (PendingTotpSecret is not { } offered
            || PendingTotpSecretAt is not { } offeredAt
            || at - offeredAt > EnrolmentOfferLifetime)
        {
            throw Refusal.Conflict(
                "There is no enrolment to confirm, or the one that was offered has gone stale. "
                + "Begin one again.");
        }

        TotpSecret = offered;
        TotpEnrolledAt = at;
        LastTotpStep = step;
        PendingTotpSecret = null;
        PendingTotpSecretAt = null;
        UpdatedAt = at;
    }

    /// <summary>Takes the second factor off, and any offer with it.</summary>
    public void DisableSecondFactor(DateTimeOffset at)
    {
        TotpSecret = null;
        TotpEnrolledAt = null;
        LastTotpStep = null;
        PendingTotpSecret = null;
        PendingTotpSecretAt = null;
        UpdatedAt = at;
    }

    /// <summary>Records a recovery through the server, for the owner to see afterwards.</summary>
    public void RecordRecovery(DateTimeOffset at)
    {
        RecoveredAt = at;
        UpdatedAt = at;
    }

    /// <summary>Remembers the step a code was accepted for, so that it cannot be used twice.</summary>
    public void RecordSecondFactorStep(long step) =>
        LastTotpStep = LastTotpStep is { } last && last >= step ? last : step;

    public void ChangeEmail(string email, DateTimeOffset at)
    {
        Email = NormalizeEmail(email);
        NormalizedEmail = NormalizeEmailForComparison(email);
        UpdatedAt = at;
    }

    /// <summary>
    /// Turns the inactivity lock on, or changes its PIN or duration. A missing
    /// hash keeps the current PIN and is only valid when the lock is already on.
    /// </summary>
    public void ConfigureInactivityLock(string? pinHash, int minutes, DateTimeOffset at)
    {
        ValidateInactivityLockMinutes(minutes);

        if (pinHash is not null && string.IsNullOrWhiteSpace(pinHash))
        {
            throw new ArgumentException("A PIN hash is required when one is supplied.", nameof(pinHash));
        }

        if (pinHash is null && InactivityLockPinHash is null)
        {
            throw Refusal.Validation("pin", "A PIN is required when the inactivity lock is turned on.");
        }

        InactivityLockPinHash = pinHash ?? InactivityLockPinHash;
        InactivityLockMinutes = minutes;
        InactivityLockVersion = checked(InactivityLockVersion + 1);
        UpdatedAt = at;
    }

    /// <summary>Turns the additional lock off and removes the only stored derivative of its PIN.</summary>
    public void DisableInactivityLock(DateTimeOffset at)
    {
        InactivityLockPinHash = null;
        InactivityLockVersion = checked(InactivityLockVersion + 1);
        UpdatedAt = at;
    }

    /// <summary>Until when this proof is delayed after wrong attempts, if it is.</summary>
    public DateTimeOffset? UnlockBlockedUntil(UnlockCredential credential, DateTimeOffset now)
    {
        var blocked = credential == UnlockCredential.Pin
            ? PinUnlockBlockedUntil
            : PasswordUnlockBlockedUntil;

        return blocked > now ? blocked : null;
    }

    /// <summary>
    /// Remembers one wrong proof across sessions and process restarts. Early
    /// failures receive a short exponential delay; the fifth holds this proof
    /// for the remainder of a fifteen-minute window. PIN and password have
    /// separate budgets so the password remains a recovery path for a guessed PIN.
    /// </summary>
    public DateTimeOffset RecordFailedUnlock(UnlockCredential credential, DateTimeOffset now)
    {
        var window = credential == UnlockCredential.Pin
            ? PinUnlockWindowStartedAt
            : PasswordUnlockWindowStartedAt;
        var failures = credential == UnlockCredential.Pin
            ? PinUnlockFailures
            : PasswordUnlockFailures;

        if (window is null || now - window.Value >= UnlockAttemptWindow)
        {
            window = now;
            failures = 0;
        }

        failures++;
        var delay = failures >= UnlockAttemptLimit
            ? UnlockAttemptWindow - (now - window.Value)
            : TimeSpan.FromSeconds(Math.Pow(2, failures - 1));
        var blockedUntil = now + (delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1));

        if (credential == UnlockCredential.Pin)
        {
            PinUnlockFailures = failures;
            PinUnlockWindowStartedAt = window;
            PinUnlockBlockedUntil = blockedUntil;
        }
        else
        {
            PasswordUnlockFailures = failures;
            PasswordUnlockWindowStartedAt = window;
            PasswordUnlockBlockedUntil = blockedUntil;
        }

        return blockedUntil;
    }

    public void RecordSuccessfulUnlock(UnlockCredential credential)
    {
        if (credential == UnlockCredential.Pin)
        {
            PinUnlockFailures = 0;
            PinUnlockWindowStartedAt = null;
            PinUnlockBlockedUntil = null;
        }
        else
        {
            PasswordUnlockFailures = 0;
            PasswordUnlockWindowStartedAt = null;
            PasswordUnlockBlockedUntil = null;
        }
    }

    /// <summary>The PIN exactly as accepted: four to six ASCII digits, including leading zeroes.</summary>
    public static string ValidateInactivityLockPin(string? pin)
    {
        if (pin is null
            || pin.Length is < PinMinLength or > PinMaxLength
            || pin.Any(character => character is < '0' or > '9'))
        {
            throw Refusal.Validation(
                "pin", $"A PIN is {PinMinLength} to {PinMaxLength} ASCII digits.");
        }

        return pin;
    }

    public static int ValidateInactivityLockMinutes(int minutes)
    {
        if (minutes is < MinInactivityLockMinutes or > MaxInactivityLockMinutes)
        {
            throw Refusal.Validation(
                "inactivity_minutes",
                $"Inactivity is {MinInactivityLockMinutes} to {MaxInactivityLockMinutes} whole minutes.");
        }

        return minutes;
    }

    /// <summary>
    /// The address as it would be stored: trimmed, composed, and recognisable
    /// as an address.
    /// </summary>
    /// <exception cref="Refusal">It is not one, or it is over its limit.</exception>
    public static string NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim().Normalize(System.Text.NormalizationForm.FormKC);

        if (string.IsNullOrEmpty(trimmed))
        {
            throw Refusal.Validation("email", "An email address is required.");
        }

        if (trimmed.Length > EmailMaxLength)
        {
            throw Refusal.Validation(
                "email", $"An email address is at most {EmailMaxLength} characters.");
        }

        // MailAddress accepts a display name in front of the address — the
        // comparison is what refuses `Owner <owner@example.com>`, which is a
        // header and not a login identifier.
        return !System.Net.Mail.MailAddress.TryCreate(trimmed, out var parsed)
            || !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase)
            ? throw Refusal.Validation("email", "An email address is required.")
            : trimmed;
    }

    /// <summary>
    /// What sign-in compares: the address folded to lower case, so that the
    /// owner is not locked out by the shift key.
    /// </summary>
    public static string NormalizeEmailForComparison(string? email) =>
        NormalizeEmail(email).ToLowerInvariant();
}
