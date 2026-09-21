using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// The rules of the one human an instance belongs to (<c>CONTEXT.md</c>,
/// Owner): what an address has to be, and what changing one moves.
/// </summary>
public sealed class OwnerTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("owner@example.com", "owner@example.com")]
    [InlineData("  owner@example.com  ", "owner@example.com")]
    [InlineData("Owner@Example.com", "Owner@Example.com")]
    public void An_address_is_kept_as_it_was_written_once_it_is_trimmed(string written, string kept) =>
        Assert.Equal(kept, Owner.NormalizeEmail(written));

    [Fact]
    public void The_shift_key_does_not_lock_the_owner_out() =>
        Assert.Equal(
            Owner.NormalizeEmailForComparison("owner@example.com"),
            Owner.NormalizeEmailForComparison("  OWNER@Example.COM "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("owner")]
    [InlineData("owner@")]
    [InlineData("@example.com")]
    [InlineData("owner at example.com")]
    // A display name in front of an address is a mail header, not a login
    // identifier, and MailAddress would otherwise accept it.
    [InlineData("Owner <owner@example.com>")]
    public void What_is_not_an_address_is_refused_as_validation(string? written)
    {
        var refusal = Assert.Throws<Refusal>(() => Owner.NormalizeEmail(written));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains("email", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_address_over_its_limit_says_so_rather_than_being_truncated()
    {
        var written = new string('a', Owner.EmailMaxLength) + "@example.com";

        var refusal = Assert.Throws<Refusal>(() => Owner.NormalizeEmail(written));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains($"{Owner.EmailMaxLength} characters", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_claimed_instance_has_an_owner_whose_two_timestamps_agree()
    {
        var owner = Owner.Claim("Owner@example.com", "$argon2id$…", Noon);

        Assert.Equal("Owner@example.com", owner.Email);
        Assert.Equal("owner@example.com", owner.NormalizedEmail);
        Assert.True(owner.Singleton);
        Assert.Equal(Noon, owner.CreatedAt);
        Assert.Equal(Noon, owner.UpdatedAt);
    }

    [Fact]
    public void Changing_the_password_moves_the_owner_forward_and_keeps_the_address()
    {
        var owner = Owner.Claim("owner@example.com", "$argon2id$first", Noon);

        owner.ChangePassword("$argon2id$second", Noon.AddDays(1));

        Assert.Equal("$argon2id$second", owner.PasswordHash);
        Assert.Equal(Noon, owner.CreatedAt);
        Assert.Equal(Noon.AddDays(1), owner.UpdatedAt);
        Assert.Equal("owner@example.com", owner.Email);
    }

    [Fact]
    public void An_owner_is_never_stored_without_a_hash() =>
        Assert.Throws<ArgumentException>(() => Owner.Claim("owner@example.com", "  ", Noon));

    [Theory]
    [InlineData("0000")]
    [InlineData("12345")]
    [InlineData("999999")]
    public void A_pin_is_kept_as_text_including_leading_zeroes(string pin) =>
        Assert.Equal(pin, Owner.ValidateInactivityLockPin(pin));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12 34")]
    [InlineData("１２３４")]
    [InlineData("12a4")]
    public void A_pin_that_is_not_four_to_six_ascii_digits_is_refused(string? pin)
    {
        var refusal = Assert.Throws<Refusal>(() => Owner.ValidateInactivityLockPin(pin));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains("pin", refusal.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void An_inactivity_period_outside_one_day_is_refused(int minutes)
    {
        var refusal = Assert.Throws<Refusal>(() => Owner.ValidateInactivityLockMinutes(minutes));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains("inactivity_minutes", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Lock_configuration_has_a_safe_default_and_each_change_moves_its_version()
    {
        var owner = Owner.Claim("owner@example.com", "$argon2id$password", Noon);

        Assert.False(owner.InactivityLockEnabled);
        Assert.Equal(Owner.DefaultInactivityLockMinutes, owner.InactivityLockMinutes);
        Assert.Equal(0, owner.InactivityLockVersion);

        owner.ConfigureInactivityLock("$argon2id$pin-one", 17, Noon.AddMinutes(1));

        Assert.True(owner.InactivityLockEnabled);
        Assert.Equal("$argon2id$pin-one", owner.InactivityLockPinHash);
        Assert.Equal(17, owner.InactivityLockMinutes);
        Assert.Equal(1, owner.InactivityLockVersion);

        owner.ConfigureInactivityLock(pinHash: null, 18, Noon.AddMinutes(2));

        Assert.Equal("$argon2id$pin-one", owner.InactivityLockPinHash);
        Assert.Equal(18, owner.InactivityLockMinutes);
        Assert.Equal(2, owner.InactivityLockVersion);

        owner.DisableInactivityLock(Noon.AddMinutes(3));

        Assert.False(owner.InactivityLockEnabled);
        Assert.Null(owner.InactivityLockPinHash);
        Assert.Equal(3, owner.InactivityLockVersion);
    }

    [Fact]
    public void Wrong_pin_attempts_are_delayed_persistently_without_blocking_the_password_path()
    {
        var owner = Owner.Claim("owner@example.com", "$argon2id$password", Noon);

        var first = owner.RecordFailedUnlock(UnlockCredential.Pin, Noon);

        Assert.Equal(Noon.AddSeconds(1), first);
        Assert.Equal(first, owner.UnlockBlockedUntil(UnlockCredential.Pin, Noon));
        Assert.Null(owner.UnlockBlockedUntil(UnlockCredential.Password, Noon));

        owner.RecordSuccessfulUnlock(UnlockCredential.Pin);

        Assert.Null(owner.UnlockBlockedUntil(UnlockCredential.Pin, Noon));
        Assert.Equal(0, owner.PinUnlockFailures);
    }

    [Fact]
    public void The_fifth_wrong_proof_holds_only_that_proof_for_the_rest_of_the_window()
    {
        var owner = Owner.Claim("owner@example.com", "$argon2id$password", Noon);
        var now = Noon;

        for (var attempt = 0; attempt < Owner.UnlockAttemptLimit; attempt++)
        {
            owner.RecordFailedUnlock(UnlockCredential.Pin, now);
            now = owner.PinUnlockBlockedUntil!.Value;
        }

        Assert.Equal(Noon.Add(Owner.UnlockAttemptWindow), owner.PinUnlockBlockedUntil);
        Assert.Null(owner.PasswordUnlockBlockedUntil);
        Assert.Null(owner.UnlockBlockedUntil(
            UnlockCredential.Pin, Noon.Add(Owner.UnlockAttemptWindow)));
    }
}
