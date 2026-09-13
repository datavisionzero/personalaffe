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

    /// <summary>When the owner last changed: the address, or the password.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

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

    public void ChangeEmail(string email, DateTimeOffset at)
    {
        Email = NormalizeEmail(email);
        NormalizedEmail = NormalizeEmailForComparison(email);
        UpdatedAt = at;
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
