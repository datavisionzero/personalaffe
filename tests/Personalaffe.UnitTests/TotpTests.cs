using System.Text;
using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// The second factor against the vectors in RFC 6238, and against the two
/// things the RFC leaves to the implementation: how wide the window is, and
/// whether a code can be used twice.
/// </summary>
public sealed class TotpTests
{
    /// <summary>
    /// The RFC's own key: the ASCII "12345678901234567890", base32 as an
    /// authenticator would be given it.
    /// </summary>
    private static readonly string Key = Base32.Encode(Encoding.ASCII.GetBytes("12345678901234567890"));

    [Theory]
    // RFC 6238, Appendix B, the SHA-1 rows. The published values are eight
    // digits and six-digit TOTP is the same number modulo a million, which is
    // to say the last six of each.
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void The_codes_are_the_ones_the_rfc_publishes(long unixTime, string expected)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unixTime);

        Assert.Equal(expected, Totp.CodeFor(Key, Totp.StepAt(at)));
    }

    [Fact]
    public void A_generated_secret_is_base32_and_the_size_the_algorithm_is_built_on()
    {
        var secret = Totp.GenerateSecret();

        Assert.Equal(Totp.SecretBytes, Base32.Decode(secret).Length);
        Assert.NotEqual(secret, Totp.GenerateSecret());
    }

    [Fact]
    public void A_code_from_this_step_is_accepted_and_says_which_step_it_was()
    {
        var now = DateTimeOffset.UtcNow;
        var step = Totp.StepAt(now);

        Assert.True(Totp.Verify(Key, Totp.CodeFor(Key, step), now, after: null, out var accepted));
        Assert.Equal(step, accepted);
    }

    [Fact]
    public void One_step_either_side_is_forgiven_and_two_are_not()
    {
        var now = DateTimeOffset.UtcNow;
        var step = Totp.StepAt(now);

        // A phone whose clock is a little out, and a person who typed the last
        // digit as the minute turned.
        Assert.True(Totp.Verify(Key, Totp.CodeFor(Key, step - 1), now, null, out _));
        Assert.True(Totp.Verify(Key, Totp.CodeFor(Key, step + 1), now, null, out _));

        // Wider than that and every extra step is another live code.
        Assert.False(Totp.Verify(Key, Totp.CodeFor(Key, step - 2), now, null, out _));
        Assert.False(Totp.Verify(Key, Totp.CodeFor(Key, step + 2), now, null, out _));
    }

    [Fact]
    public void A_code_already_used_is_refused_however_correct_it_is()
    {
        var now = DateTimeOffset.UtcNow;
        var step = Totp.StepAt(now);

        Assert.True(Totp.Verify(Key, Totp.CodeFor(Key, step), now, after: null, out _));
        Assert.False(Totp.Verify(Key, Totp.CodeFor(Key, step), now, after: step, out _));

        // And so is the one before it, which is still inside the window.
        Assert.False(Totp.Verify(Key, Totp.CodeFor(Key, step - 1), now, after: step, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("000000")]
    public void What_is_not_a_code_of_this_secret_is_refused(string? code) =>
        Assert.False(Totp.Verify(Key, code, DateTimeOffset.UtcNow, null, out _));

    [Fact]
    public void Without_a_secret_nothing_is_a_code() =>
        Assert.False(Totp.Verify(null, "287082", DateTimeOffset.UtcNow, null, out _));

    [Fact]
    public void Spaces_in_a_typed_code_are_the_typing_and_not_the_code()
    {
        var now = DateTimeOffset.UtcNow;
        var code = Totp.CodeFor(Key, Totp.StepAt(now));

        Assert.True(Totp.Verify(Key, $" {code[..3]} {code[3..]} ", now, null, out _));
    }

    [Fact]
    public void The_uri_says_what_an_authenticator_has_to_be_told()
    {
        var uri = Totp.UriFor(Key, "owner@example.com");

        Assert.StartsWith("otpauth://totp/personalaffe:owner%40example.com?", uri, StringComparison.Ordinal);
        Assert.Contains($"secret={Key}", uri, StringComparison.Ordinal);
        Assert.Contains("algorithm=SHA1", uri, StringComparison.Ordinal);
        Assert.Contains("digits=6", uri, StringComparison.Ordinal);
        Assert.Contains("period=30", uri, StringComparison.Ordinal);
    }
}
