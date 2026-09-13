using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>Whoever the door admitted, and the one question every act asks of them.</summary>
public sealed class CallerTests
{
    [Fact]
    public void The_owner_in_a_browser_carries_the_session_they_came_in_on()
    {
        var session = Guid.CreateVersion7();
        var caller = Caller.Owner(Guid.CreateVersion7(), session);

        Assert.Equal(CallerKind.Owner, caller.Kind);
        Assert.True(caller.IsOwner);
        Assert.Equal(session, caller.SessionId);
    }

    [Fact]
    public void The_owner_holding_a_token_carries_no_session()
    {
        var caller = Caller.Owner(Guid.CreateVersion7());

        Assert.Null(caller.SessionId);
        Assert.Same(caller, caller.RequireOwner("change a security setting"));
    }

    [Fact]
    public void Anybody_who_is_not_the_owner_is_refused_by_name_of_the_act()
    {
        var agent = Caller.Owner(Guid.CreateVersion7()) with { Kind = CallerKind.Agent };

        var refusal = Assert.Throws<Refusal>(() => agent.RequireOwner("issue a credential"));

        Assert.Equal(RefusalCode.Forbidden, refusal.Code);
        Assert.Contains("issue a credential", refusal.Detail, StringComparison.Ordinal);
    }
}
