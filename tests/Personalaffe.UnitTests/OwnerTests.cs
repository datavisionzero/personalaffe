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
}
