using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Personalaffe.Api.Http;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The home page: what is on it, what is in it, and what a hidden tile, a
/// switched-off application and an agent each take away.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DashboardTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_fresh_instance_draws_every_tile()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        var tiles = (await workspace.DashboardAsync(TestContext.Current.CancellationToken))
            ["tiles"]!.AsArray();

        // The order is the fixed layout's, and every one of them is shown and
        // offered: a workspace nobody configured is a whole workspace.
        Assert.Equal(
            ["tasks", "knowledge", "scratchpad", "files", "weather", "bookmarks"],
            tiles.Select(tile => tile!["tile"]!.GetValue<string>()));

        Assert.All(tiles, tile =>
        {
            Assert.True(tile!["shown"]!.GetValue<bool>());
            Assert.True(tile["offered"]!.GetValue<bool>());
        });
    }

    [Fact]
    public async Task Each_drawn_tile_carries_what_is_useful_now()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        var list = await workspace.ListAsync("Work", TestContext.Current.CancellationToken);
        await workspace.TaskAsync(
            list, "Send the invoice", string.Empty, null, TestContext.Current.CancellationToken);
        await workspace.PageAsync("Architecture", "…", TestContext.Current.CancellationToken);
        await workspace.EntryAsync("a code from the bank", TestContext.Current.CancellationToken);
        await workspace.FileAsync("invoice.pdf", TestContext.Current.CancellationToken);

        var home = await workspace.DashboardAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Send the invoice", home["tasks"]![0]!["title"]!.GetValue<string>());

        // A task on the home page is out of its list, so the list is beside it.
        Assert.Equal("Work", home["tasks"]![0]!["list"]!.GetValue<string>());
        Assert.Equal("Architecture", home["knowledge"]![0]!["title"]!.GetValue<string>());
        Assert.Equal("a code from the bank", home["scratchpad"]![0]!["preview"]!.GetValue<string>());
        Assert.Equal("invoice.pdf", home["files"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task What_is_open_comes_soonest_due_first_and_the_undated_after_it()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        var list = await workspace.ListAsync("Work", TestContext.Current.CancellationToken);

        await workspace.TaskAsync(
            list, "Someday", string.Empty, null, TestContext.Current.CancellationToken);
        await workspace.TaskAsync(
            list, "Next week", string.Empty, new DateOnly(2026, 9, 23),
            TestContext.Current.CancellationToken);
        await workspace.TaskAsync(
            list, "Today", string.Empty, new DateOnly(2026, 9, 16),
            TestContext.Current.CancellationToken);

        var tasks = (await workspace.DashboardAsync(TestContext.Current.CancellationToken))
            ["tasks"]!.AsArray();

        Assert.Equal(
            ["Today", "Next week", "Someday"],
            tasks.Select(task => task!["title"]!.GetValue<string>()));
    }

    [Fact]
    public async Task What_is_in_the_Trash_is_not_on_the_home_page()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        var page = await workspace.PageAsync(
            "Architecture", "…", TestContext.Current.CancellationToken);

        await workspace.DeleteAsync($"/api/knowledge/pages/{page}", TestContext.Current.CancellationToken);

        var home = await workspace.DashboardAsync(TestContext.Current.CancellationToken);

        // The tile is drawn and holds nothing, which is not the same answer as
        // a tile that is not drawn at all.
        Assert.Empty(home["knowledge"]!.AsArray());
    }

    [Fact]
    public async Task Hiding_a_tile_takes_its_content_away_and_leaves_the_tile()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync("Architecture", "…", TestContext.Current.CancellationToken);

        var hidden = await ShowAsync(workspace, "knowledge", shown: false);

        Assert.False(hidden["shown"]!.GetValue<bool>());
        Assert.True(hidden["offered"]!.GetValue<bool>());

        var home = await workspace.DashboardAsync(TestContext.Current.CancellationToken);

        Assert.Null(home["knowledge"]);
        Assert.Contains(
            home["tiles"]!.AsArray(),
            tile => tile!["tile"]!.GetValue<string>() == "knowledge");
    }

    [Fact]
    public async Task A_switched_off_application_stops_offering_its_tile_and_keeps_the_setting()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync("Architecture", "…", TestContext.Current.CancellationToken);
        await SwitchAsync(workspace, "knowledge", enabled: false);

        var off = Tile(await workspace.DashboardAsync(TestContext.Current.CancellationToken), "knowledge");

        Assert.False(off["offered"]!.GetValue<bool>());
        Assert.True(off["shown"]!.GetValue<bool>());

        // Switching it back on brings the tile back as it was, because what was
        // stored is the owner's preference and not a consequence of the switch.
        await SwitchAsync(workspace, "knowledge", enabled: true);

        var home = await workspace.DashboardAsync(TestContext.Current.CancellationToken);

        Assert.True(Tile(home, "knowledge")["offered"]!.GetValue<bool>());
        Assert.Single(home["knowledge"]!.AsArray());
    }

    [Fact]
    public async Task A_tile_of_a_switched_off_application_can_still_be_hidden()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await SwitchAsync(workspace, "files", enabled: false);

        var hidden = await ShowAsync(workspace, "files", shown: false);

        Assert.False(hidden["shown"]!.GetValue<bool>());
        Assert.False(hidden["offered"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_agent_sees_the_tiles_of_what_it_can_read_and_no_others()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync("Architecture", "…", TestContext.Current.CancellationToken);
        await workspace.EntryAsync("a code from the bank", TestContext.Current.CancellationToken);

        using var agent = await AnAgentAsync(workspace, knowledge: "read");

        var home = JsonNode.Parse(await agent.GetStringAsync(
            "/api/dashboard", TestContext.Current.CancellationToken))!;

        Assert.True(Tile(home, "knowledge")["offered"]!.GetValue<bool>());
        Assert.False(Tile(home, "scratchpad")["offered"]!.GetValue<bool>());

        Assert.Single(home["knowledge"]!.AsArray());
        Assert.Null(home["scratchpad"]);
        Assert.Null(home["tasks"]);
        Assert.Null(home["files"]);

        // The weather belongs to no application, so it is offered to anybody
        // the door let in.
        Assert.True(Tile(home, "weather")["offered"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_agent_cannot_decide_what_is_on_the_owners_home_page()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        using var agent = await AnAgentAsync(workspace, knowledge: "read_write");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/dashboard/tiles/knowledge")
        {
            Content = JsonContent.Create(new { shown = false }),
        };
        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch, await VersionOfAsync(workspace, "knowledge"));

        using var response = await agent.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_hide_holding_an_older_version_is_refused()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        var stale = await VersionOfAsync(workspace, "knowledge");

        await ShowAsync(workspace, "knowledge", shown: false);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/dashboard/tiles/knowledge")
        {
            Content = JsonContent.Create(new { shown = true }),
        };
        request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, stale);

        using var response = await workspace.Owner.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task A_word_that_names_no_tile_is_refused()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/dashboard/tiles/finance")
        {
            Content = JsonContent.Create(new { shown = false }),
        };
        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch, await VersionOfAsync(workspace, "knowledge"));

        using var response = await workspace.Owner.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static JsonNode Tile(JsonNode home, string tile) =>
        home["tiles"]!.AsArray().First(one => one!["tile"]!.GetValue<string>() == tile)!;

    private static async Task<string> VersionOfAsync(AWorkspace workspace, string tile) =>
        $"\"{Rfc3339.Spell(Tile(
            await workspace.DashboardAsync(TestContext.Current.CancellationToken),
            tile)["updated_at"]!.GetValue<DateTimeOffset>())}\"";

    private static async Task<JsonNode> ShowAsync(AWorkspace workspace, string tile, bool shown)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/dashboard/tiles/{tile}")
        {
            Content = JsonContent.Create(new { shown }),
        };
        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch, await VersionOfAsync(workspace, tile));

        using var response = await workspace.Owner.SendAsync(
            request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task SwitchAsync(AWorkspace workspace, string application, bool enabled)
    {
        var state = JsonNode.Parse(await workspace.Owner.GetStringAsync(
            "/api/applications", TestContext.Current.CancellationToken))!
            ["items"]!.AsArray()
            .First(item => item!["application"]!.GetValue<string>() == application)!;

        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/applications/{application}")
        {
            Content = JsonContent.Create(new { enabled }),
        };
        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch,
            $"\"{Rfc3339.Spell(state["updated_at"]!.GetValue<DateTimeOffset>())}\"");

        using var response = await workspace.Owner.SendAsync(
            request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpClient> AnAgentAsync(AWorkspace workspace, string knowledge)
    {
        using var granted = await workspace.Owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new
                {
                    scratchpad = "none",
                    knowledge,
                    tasks = "none",
                    files = "none",
                },
            },
            TestContext.Current.CancellationToken);

        granted.EnsureSuccessStatusCode();

        var token = JsonNode.Parse(await granted.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["token"]!.GetValue<string>();

        var client = workspace.Instance.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
