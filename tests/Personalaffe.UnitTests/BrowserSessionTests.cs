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

    [Fact]
    public void The_inactivity_boundary_is_closed_and_a_late_activity_report_cannot_reopen_it()
    {
        var owner = Personalaffe.Domain.Owner.Claim("owner@example.com", "$argon2id$password", Noon);
        owner.ConfigureInactivityLock("$argon2id$pin", 5, Noon);
        var (session, _) = BrowserSession.Begin(
            Owner, null, Noon, owner.InactivityLockVersion);

        Assert.False(session.InactivityState(owner, Noon.AddMinutes(5).AddTicks(-1)).Locked);
        Assert.True(session.InactivityState(owner, Noon.AddMinutes(5)).Locked);
        Assert.False(session.RecordInteraction(owner, Noon.AddMinutes(5)));

        session.MarkInactivityLocked(Noon.AddMinutes(5));
        Assert.True(session.InactivityState(owner, Noon.AddMinutes(4)).Locked);
    }

    [Fact]
    public void Only_explicit_activity_moves_the_inactivity_deadline()
    {
        var owner = Personalaffe.Domain.Owner.Claim("owner@example.com", "$argon2id$password", Noon);
        owner.ConfigureInactivityLock("$argon2id$pin", 5, Noon);
        var (session, _) = BrowserSession.Begin(
            Owner, null, Noon, owner.InactivityLockVersion);

        session.Touch(Noon.AddMinutes(4));
        Assert.Equal(Noon, session.LastInteractionAt);

        Assert.True(session.RecordInteraction(owner, Noon.AddMinutes(4)));
        Assert.Equal(Noon.AddMinutes(4), session.LastInteractionAt);
        Assert.False(session.InactivityState(owner, Noon.AddMinutes(8)).Locked);
    }

    [Fact]
    public void A_new_pin_configuration_closes_an_old_session_until_it_is_explicitly_unlocked()
    {
        var owner = Personalaffe.Domain.Owner.Claim("owner@example.com", "$argon2id$password", Noon);
        owner.ConfigureInactivityLock("$argon2id$first", 5, Noon);
        var (session, _) = BrowserSession.Begin(
            Owner, null, Noon, owner.InactivityLockVersion);

        owner.ConfigureInactivityLock("$argon2id$second", 5, Noon.AddMinutes(1));

        Assert.True(session.InactivityState(owner, Noon.AddMinutes(1)).Locked);

        session.Unlock(owner.InactivityLockVersion, Noon.AddMinutes(1));

        Assert.False(session.InactivityState(owner, Noon.AddMinutes(1)).Locked);
        Assert.Equal(Noon.AddMinutes(1), session.LastInteractionAt);
    }
}
