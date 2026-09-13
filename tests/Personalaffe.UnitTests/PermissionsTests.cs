using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>What a caller may do, one answer per application.</summary>
public sealed class PermissionsTests
{
    [Fact]
    public void Nothing_reaches_nothing()
    {
        foreach (var application in Enum.GetValues<WorkspaceApplication>())
        {
            Assert.False(Permissions.None.MayRead(application));
            Assert.False(Permissions.None.MayWrite(application));
        }
    }

    [Fact]
    public void The_owners_reaches_everything()
    {
        foreach (var application in Enum.GetValues<WorkspaceApplication>())
        {
            Assert.True(Permissions.Full.MayRead(application));
            Assert.True(Permissions.Full.MayWrite(application));
        }
    }

    [Fact]
    public void Read_reads_and_does_not_write()
    {
        var granted = Permissions.None with { Knowledge = Permission.Read };

        Assert.True(granted.MayRead(WorkspaceApplication.Knowledge));
        Assert.False(granted.MayWrite(WorkspaceApplication.Knowledge));

        // And says nothing about anywhere else.
        Assert.False(granted.MayRead(WorkspaceApplication.Tasks));
    }

    [Fact]
    public void Every_application_has_an_answer()
    {
        var granted = new Permissions(
            Permission.Read, Permission.ReadWrite, Permission.None, Permission.Read);

        Assert.Equal(Permission.Read, granted.For(WorkspaceApplication.Scratchpad));
        Assert.Equal(Permission.ReadWrite, granted.For(WorkspaceApplication.Knowledge));
        Assert.Equal(Permission.None, granted.For(WorkspaceApplication.Tasks));
        Assert.Equal(Permission.Read, granted.For(WorkspaceApplication.Files));
    }

    [Fact]
    public void An_agent_is_refused_by_name_of_the_application()
    {
        var caller = Caller.Agent(
            AgentAccess.Grant(
                "an agent",
                Permissions.None with { Knowledge = Permission.Read },
                DateTimeOffset.UtcNow).Access);

        Assert.Same(caller, caller.RequireRead(WorkspaceApplication.Knowledge));

        var readOnly = Assert.Throws<Refusal>(
            () => caller.RequireWrite(WorkspaceApplication.Knowledge));
        Assert.Equal(RefusalCode.Forbidden, readOnly.Code);
        Assert.Contains("does not change it", readOnly.Detail, StringComparison.Ordinal);

        var absent = Assert.Throws<Refusal>(() => caller.RequireRead(WorkspaceApplication.Tasks));
        Assert.Contains("does not reach tasks", absent.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_agent_is_never_the_owner()
    {
        var caller = Caller.Agent(
            AgentAccess.Grant("an agent", Permissions.Full, DateTimeOffset.UtcNow).Access);

        Assert.False(caller.IsOwner);
        Assert.Throws<Refusal>(() => caller.RequireOwner("issue a credential"));
    }
}
