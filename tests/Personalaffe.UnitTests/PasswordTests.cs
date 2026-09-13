using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// What personalaffe asks of a password, which is length and nothing else
/// (<see cref="Password"/>).
/// </summary>
public sealed class PasswordTests
{
    [Fact]
    public void A_password_of_the_minimum_length_is_a_password()
    {
        var written = new string('x', Password.MinLength);

        Assert.Equal(written, Password.Checked(written));
    }

    [Fact]
    public void A_long_passphrase_is_not_improved_by_being_shortened()
    {
        var written = "correct horse battery staple, and then some more of it";

        Assert.Equal(written, Password.Checked(written));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_is_not_a_password(string? written)
    {
        var refusal = Assert.Throws<Refusal>(() => Password.Checked(written));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Fact]
    public void One_character_short_is_refused_with_the_rule_in_the_sentence()
    {
        var refusal = Assert.Throws<Refusal>(() => Password.Checked(new string('x', Password.MinLength - 1)));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains($"{Password.MinLength} to {Password.MaxLength}", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_megabyte_is_refused_before_it_becomes_a_megabyte_of_argon2()
    {
        var refusal = Assert.Throws<Refusal>(() => Password.Checked(new string('x', Password.MaxLength + 1)));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Fact]
    public void The_field_it_names_is_the_field_the_caller_sent()
    {
        var refusal = Assert.Throws<Refusal>(() => Password.Checked("short", "new_password"));

        Assert.Contains("new_password", refusal.Detail, StringComparison.Ordinal);
    }
}
