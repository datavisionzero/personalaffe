using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// A named, revocable authorization for an agent acting on the owner's behalf
/// (<c>CONTEXT.md</c>): what it is called, what it reaches, and what revoking
/// it does.
/// </summary>
public sealed class AgentAccessTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_granted_access_carries_a_token_that_is_returned_once()
    {
        var (access, secret) = AgentAccess.Grant(
            " the deploy agent ", Permissions.None with { Knowledge = Permission.Read }, Noon);

        Assert.Equal("the deploy agent", access.Name);
        Assert.Equal(Permission.Read, access.Permissions.Knowledge);
        Assert.Equal(Permission.None, access.Permissions.Tasks);
        Assert.Equal(TokenSecret.HashOf(secret), access.TokenHash);
        Assert.Equal(secret[..TokenSecret.PrefixLength], access.TokenPrefix);
        Assert.StartsWith(TokenSecret.Prefix, secret, StringComparison.Ordinal);
        Assert.False(access.Revoked);
        Assert.Null(access.LastUsedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Agent_access_has_a_name(string? name)
    {
        var refusal = Assert.Throws<Refusal>(
            () => AgentAccess.Grant(name, Permissions.None, Noon));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Fact]
    public void A_name_over_its_limit_says_so()
    {
        var refusal = Assert.Throws<Refusal>(
            () => AgentAccess.Grant(new string('a', AgentAccess.NameMaxLength + 1), Permissions.None, Noon));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Fact]
    public void Reissuing_replaces_the_token_and_forgets_that_the_old_one_was_used()
    {
        var (access, first) = AgentAccess.Grant("an agent", Permissions.Full, Noon);
        access.Used(Noon.AddHours(1));

        var second = access.ReissueToken(Noon.AddHours(2));

        Assert.NotEqual(first, second);
        Assert.Equal(TokenSecret.HashOf(second), access.TokenHash);
        Assert.Null(access.LastUsedAt);
        Assert.Equal(Noon.AddHours(2), access.TokenIssuedAt);
    }

    [Fact]
    public void Revoking_is_a_timestamp_and_not_a_deletion()
    {
        var (access, _) = AgentAccess.Grant("an agent", Permissions.Full, Noon);

        access.Revoke(Noon.AddDays(1));
        access.Revoke(Noon.AddDays(2));

        Assert.True(access.Revoked);
        Assert.Equal(Noon.AddDays(1), access.RevokedAt);
        Assert.Equal("an agent", access.Name);

        // And a revoked access is not "used" by anything that presents its
        // token afterwards.
        Assert.False(access.Used(Noon.AddDays(3)));
    }

    [Fact]
    public void Being_used_is_written_down_at_most_every_few_minutes()
    {
        var (access, _) = AgentAccess.Grant("an agent", Permissions.Full, Noon);

        Assert.True(access.Used(Noon));
        Assert.False(access.Used(Noon.AddMinutes(1)));
        Assert.True(access.Used(Noon + AgentAccess.UseInterval + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_token_is_recognisable_before_the_database_is_asked()
    {
        Assert.True(TokenSecret.IsAcceptable(TokenSecret.Generate()));

        Assert.False(TokenSecret.IsAcceptable(null));
        Assert.False(TokenSecret.IsAcceptable(""));
        Assert.False(TokenSecret.IsAcceptable("pea_short"));
        Assert.False(TokenSecret.IsAcceptable(new string('x', TokenSecret.Length)));
    }

    [Fact]
    public void Two_tokens_are_never_the_same()
    {
        var many = Enumerable.Range(0, 100).Select(_ => TokenSecret.Generate()).ToList();

        Assert.Equal(100, many.Distinct(StringComparer.Ordinal).Count());
    }
}
