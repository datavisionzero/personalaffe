using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>The rules of the switch, without a database under them.</summary>
public sealed class ApplicationStateTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_application_starts_switched_on()
    {
        var state = ApplicationState.Fresh(WorkspaceApplication.Knowledge, Noon);

        Assert.Equal(WorkspaceApplication.Knowledge, state.Application);
        Assert.True(state.Enabled);
        Assert.Equal(Noon, state.UpdatedAt);
    }

    [Fact]
    public void Switching_it_moves_the_version()
    {
        var state = ApplicationState.Fresh(WorkspaceApplication.Tasks, Noon);
        var later = Noon.AddMinutes(5);

        Assert.True(state.Switch(enabled: false, later));
        Assert.False(state.Enabled);
        Assert.Equal(later, state.UpdatedAt);
        Assert.True(state.Version.Matches(ContentVersion.Of(later)));
    }

    [Fact]
    public void Switching_it_to_what_it_already_is_changes_nothing()
    {
        var state = ApplicationState.Fresh(WorkspaceApplication.Files, Noon);

        // Not a write, so the version a caller is holding stays good. The guard
        // is still checked before this is reached; what it saves is a row
        // written for a change that is not one.
        Assert.False(state.Switch(enabled: true, Noon.AddHours(1)));
        Assert.Equal(Noon, state.UpdatedAt);
    }

    [Fact]
    public void A_switched_off_application_refuses_with_the_code_that_says_why()
    {
        var refusal = ApplicationState.Off(WorkspaceApplication.Scratchpad);

        // Its own code, not `not-found`: the content is there and the owner can
        // have it back by switching the application on.
        Assert.Equal(RefusalCode.Disabled, refusal.Code);
        Assert.Equal("scratchpad", refusal.Extensions["application"]);
        Assert.Contains("is kept", refusal.Detail, StringComparison.Ordinal);
    }
}
