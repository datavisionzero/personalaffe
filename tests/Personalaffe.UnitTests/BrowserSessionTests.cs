using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// The two lifetimes of a signed-in browser, and the write that is deliberately
/// not made on every request.
/// </summary>
public sealed class BrowserSessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public void A_session_begins_valid_and_its_secret_is_not_in_the_row()
    {
        var (session, secret) = BrowserSession.Begin(Owner, "a-browser/1.0", Noon);

        Assert.True(session.IsValid(Noon));
        Assert.Equal(Owner, session.OwnerId);
        Assert.Equal(Noon.Add(BrowserSession.AbsoluteLifetime), session.ExpiresAt);
        Assert.Equal(BrowserSession.SecretHashLength, session.SecretHash.Length);
        Assert.Equal(BrowserSession.Hash(secret), session.SecretHash);
        Assert.NotEmpty(secret);
    }

    [Fact]
    public void Two_sessions_never_share_a_secret()
    {
        var (_, one) = BrowserSession.Begin(Owner, null, Noon);
        var (_, other) = BrowserSession.Begin(Owner, null, Noon);

        Assert.NotEqual(one, other);
    }

    [Fact]
    public void What_the_browser_called_itself_is_kept_short_and_kept_as_written()
    {
        var (unnamed, _) = BrowserSession.Begin(Owner, "   ", Noon);
        Assert.Null(unnamed.Description);

        var (long_, _) = BrowserSession.Begin(Owner, new string('u', 500), Noon);
        Assert.Equal(BrowserSession.DescriptionMaxLength, long_.Description!.Length);
    }

    [Fact]
    public void A_session_nobody_uses_ends_before_the_absolute_one_does()
    {
        var (session, _) = BrowserSession.Begin(Owner, null, Noon);

        Assert.True(session.IsValid(Noon + BrowserSession.IdleLifetime - TimeSpan.FromMinutes(1)));
        Assert.False(session.IsValid(Noon + BrowserSession.IdleLifetime + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void A_session_in_daily_use_still_ends()
    {
        var (session, _) = BrowserSession.Begin(Owner, null, Noon);

        // Used every day, which keeps the idle deadline moving; the absolute
        // one does not move, and a secret that never expires is a secret that
        // is eventually somewhere it should not be.
        for (var day = 1; day <= 40; day++)
        {
            session.Touch(Noon.AddDays(day));
        }

        Assert.False(session.IsValid(Noon.AddDays(40)));
        Assert.True(session.IsValid(Noon.AddDays(29)));
    }

    [Fact]
    public void Being_used_is_written_down_at_most_once_every_few_minutes()
    {
        var (session, _) = BrowserSession.Begin(Owner, null, Noon);

        // Otherwise every read of the workspace would be a write to the
        // database, to keep something measured in days up to date.
        Assert.False(session.Touch(Noon.AddMinutes(1)));
        Assert.False(session.Touch(Noon + BrowserSession.TouchInterval - TimeSpan.FromSeconds(1)));
        Assert.True(session.Touch(Noon + BrowserSession.TouchInterval + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Revoking_is_immediate_and_revoking_twice_keeps_the_first_moment()
    {
        var (session, _) = BrowserSession.Begin(Owner, null, Noon);

        session.Revoke(Noon.AddHours(1));
        session.Revoke(Noon.AddHours(2));

        Assert.True(session.Revoked);
        Assert.Equal(Noon.AddHours(1), session.RevokedAt);
        Assert.False(session.IsValid(Noon.AddHours(1)));
        Assert.False(session.Touch(Noon.AddDays(1)));
    }
}
