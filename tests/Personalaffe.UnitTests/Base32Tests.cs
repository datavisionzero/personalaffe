using System.Text;
using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>RFC 4648 base32, which is how a phone is given a shared secret.</summary>
public sealed class Base32Tests
{
    [Theory]
    // RFC 4648, section 10.
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void The_vectors_of_the_rfc_encode_as_the_rfc_says(string plain, string encoded) =>
        Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(plain)));

    [Theory]
    [InlineData("MZXW6YTBOI")]
    [InlineData("mzxw6ytboi")]
    [InlineData("MZXW 6YTB OI")]
    [InlineData("MZXW6YTBOI======")]
    public void A_secret_survives_being_retyped(string encoded) =>
        Assert.Equal("foobar", Encoding.ASCII.GetString(Base32.Decode(encoded)));

    [Fact]
    public void Anything_round_trips()
    {
        var bytes = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        Assert.Equal(bytes, Base32.Decode(Base32.Encode(bytes)));
    }

    [Theory]
    [InlineData("MZXW6YTB01")]
    [InlineData("!!!!")]
    public void What_is_not_base32_says_so(string encoded) =>
        Assert.Throws<ArgumentException>(() => Base32.Decode(encoded));
}
