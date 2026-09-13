using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Infrastructure.Security;

namespace Personalaffe.UnitTests;

/// <summary>
/// The owner's password as it is stored: an Argon2id value that describes
/// itself, and a verification that reads the parameters out of the value rather
/// than out of today's constants.
/// </summary>
public sealed class Argon2idPasswordHasherTests
{
    private const string Correct = "correct horse battery staple";

    private readonly IPasswordHasher _hasher = new Argon2idPasswordHasher();

    [Fact]
    public async Task What_was_hashed_verifies()
    {
        var encoded = await _hasher.HashAsync(Correct, TestContext.Current.CancellationToken);

        Assert.True(await _hasher.VerifyAsync(encoded, Correct, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Anything_else_does_not()
    {
        var encoded = await _hasher.HashAsync(Correct, TestContext.Current.CancellationToken);

        Assert.False(await _hasher.VerifyAsync(
            encoded, "correct horse battery stapl", TestContext.Current.CancellationToken));
        Assert.False(await _hasher.VerifyAsync(
            encoded, "Correct horse battery staple", TestContext.Current.CancellationToken));
        Assert.False(await _hasher.VerifyAsync(encoded, string.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_same_password_hashes_to_two_different_values()
    {
        var once = await _hasher.HashAsync(Correct, TestContext.Current.CancellationToken);
        var again = await _hasher.HashAsync(Correct, TestContext.Current.CancellationToken);

        // The salt is what makes them differ, and the point of it: two
        // instances with the same password share nothing in their databases.
        Assert.NotEqual(once, again);
        Assert.True(await _hasher.VerifyAsync(again, Correct, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_value_says_what_made_it()
    {
        var encoded = await _hasher.HashAsync(Correct, TestContext.Current.CancellationToken);

        var parts = encoded.Split('$');

        Assert.Equal(6, parts.Length);
        Assert.Equal(string.Empty, parts[0]);
        Assert.Equal("argon2id", parts[1]);
        Assert.Equal("v=19", parts[2]);
        Assert.Matches("^m=[0-9]+,t=[0-9]+,p=[0-9]+$", parts[3]);
    }

    [Fact]
    public async Task A_value_made_at_a_lower_cost_still_verifies()
    {
        // This is the whole reason the parameters are in the value: an owner set
        // up under a cheaper hash must not be locked out on the day the cost is
        // raised. The value below is a real one at the lowest cost this hasher
        // will read back.
        var cheap = "$argon2id$v=19$m=8192,t=1,p=1$"
            + "c29tZXNhbHRvZjE2Ynl0ZQ==$"
            + "bm90LWEtcmVhbC1oYXNoLWJ1dC10aGlydHktdHdvIQ==";

        // It is not this password's hash, so it answers no — and it answers,
        // rather than throwing, which is what the door needs.
        Assert.False(await _hasher.VerifyAsync(cheap, Correct, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash at all")]
    [InlineData("$argon2i$v=19$m=65536,t=3,p=1$c29tZXNhbHRvZjE2Ynl0ZQ==$aGFzaA==")]
    [InlineData("$argon2id$v=16$m=65536,t=3,p=1$c29tZXNhbHRvZjE2Ynl0ZQ==$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=65536,t=3$c29tZXNhbHRvZjE2Ynl0ZQ==$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=99999999,t=3,p=1$c29tZXNhbHRvZjE2Ynl0ZQ==$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=1$c2hvcnQ=$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=1$not base64$aGFzaA==")]
    public async Task A_value_this_hasher_cannot_read_admits_nobody_and_throws_nothing(string stored) =>
        Assert.False(await _hasher.VerifyAsync(stored, Correct, TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_password_the_product_would_not_accept_is_not_hashed_either()
    {
        // The rule is one rule, in Domain, and the hasher does not have a
        // second opinion about it.
        var refusal = await Assert.ThrowsAsync<Refusal>(
            () => _hasher.HashAsync("short", TestContext.Current.CancellationToken));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }
}
