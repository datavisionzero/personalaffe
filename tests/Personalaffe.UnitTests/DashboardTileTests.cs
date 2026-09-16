using Personalaffe.Domain;
using Personalaffe.Domain.Dashboard;

namespace Personalaffe.UnitTests;

/// <summary>
/// The home page's tiles: a closed set in a fixed order, each of them the
/// owner's to show or hide.
/// </summary>
public sealed class DashboardTileTests
{
    [Fact]
    public void A_fresh_instance_shows_every_tile()
    {
        // An instance nobody has configured is a whole workspace, not an empty
        // page with five things to find and turn on.
        var tiles = Enum.GetValues<DashboardTile>()
            .Select(tile => TileState.Fresh(tile, DateTimeOffset.UnixEpoch));

        Assert.All(tiles, tile => Assert.True(tile.Shown));
    }

    [Fact]
    public void Hiding_a_tile_is_a_write_and_carries_a_new_version()
    {
        var tile = TileState.Fresh(DashboardTile.Files, DateTimeOffset.UnixEpoch);
        var before = tile.Version;

        Assert.True(tile.Show(shown: false, DateTimeOffset.UnixEpoch.AddMinutes(1)));

        Assert.False(tile.Shown);
        Assert.False(tile.Version.Matches(before));
    }

    [Fact]
    public void Hiding_a_tile_that_is_already_hidden_changes_nothing()
    {
        var tile = TileState.Fresh(DashboardTile.Files, DateTimeOffset.UnixEpoch);
        tile.Show(shown: false, DateTimeOffset.UnixEpoch.AddMinutes(1));

        var version = tile.Version;

        // Not a write, so the version it was read at is still the version it is
        // at — and the guard in front of it has already had its say.
        Assert.False(tile.Show(shown: false, DateTimeOffset.UnixEpoch.AddMinutes(2)));
        Assert.True(tile.Version.Matches(version));
    }

    [Theory]
    [InlineData(DashboardTile.Tasks, WorkspaceApplication.Tasks)]
    [InlineData(DashboardTile.Knowledge, WorkspaceApplication.Knowledge)]
    [InlineData(DashboardTile.Scratchpad, WorkspaceApplication.Scratchpad)]
    [InlineData(DashboardTile.Files, WorkspaceApplication.Files)]
    public void Four_tiles_answer_to_an_applications_switch(
        DashboardTile tile, WorkspaceApplication application)
    {
        Assert.Equal(application, tile.Application());
    }

    [Fact]
    public void The_weather_answers_to_no_application_and_cannot_be_switched_off_with_one()
    {
        Assert.Null(DashboardTile.Weather.Application());
    }

    [Fact]
    public void Every_tile_says_which_application_it_is_or_that_it_is_nobodys()
    {
        // A sixth tile added without an answer here throws rather than
        // quietly counting as nobody's — which would make it a tile no switch
        // can ever take off the page.
        Assert.All(
            Enum.GetValues<DashboardTile>(),
            tile => Assert.Null(Record.Exception(() => tile.Application())));
    }
}
