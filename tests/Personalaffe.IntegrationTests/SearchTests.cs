using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Personalaffe.Api.Http;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// One search over four applications: what it finds, in what order, and
/// everything it must not find.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SearchTests(PostgresFixture postgres)
{
    [Fact]
    public async Task One_question_finds_something_in_every_application()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync(
            "Architecture decisions", "Where storage lives.", TestContext.Current.CancellationToken);
        await workspace.EntryAsync(
            "architecture notes from the call", TestContext.Current.CancellationToken);

        var list = await workspace.ListAsync("Work", TestContext.Current.CancellationToken);
        await workspace.TaskAsync(
            list, "Write the architecture page", string.Empty, null, TestContext.Current.CancellationToken);

        await workspace.FileAsync("architecture.pdf", TestContext.Current.CancellationToken);

        var found = (await workspace.SearchAsync("architecture", TestContext.Current.CancellationToken))
            ["items"]!.AsArray();

        Assert.Equal(
            ["files", "knowledge", "scratchpad", "tasks"],
            found.Select(item => item!["application"]!.GetValue<string>()).Order());
    }

    [Fact]
    public async Task What_a_thing_is_called_outranks_what_it_mentions()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync(
            "Holiday", "We talked about the architecture of the hotel.",
            TestContext.Current.CancellationToken);
        await workspace.PageAsync(
            "Architecture", "Nothing much yet.", TestContext.Current.CancellationToken);

        // The index carries the weights and ts_rank reads them; nothing in the
        // application reorders anything.
        Assert.Equal(
            ["Architecture", "Holiday"],
            await workspace.FoundAsync("architecture", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_word_is_a_beginning_and_all_of_them_have_to_be_found()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync(
            "Architecture decisions", string.Empty, TestContext.Current.CancellationToken);
        await workspace.PageAsync("Architecture", string.Empty, TestContext.Current.CancellationToken);

        // Two prefixes, and only the page that carries both.
        Assert.Equal(
            ["Architecture decisions"],
            await workspace.FoundAsync("arch dec", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_file_is_found_by_any_word_of_its_name_and_never_by_what_is_in_it()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.FileAsync(
            "Budget-2026.final.pdf",
            TestContext.Current.CancellationToken,
            content: "a secret word nobody indexed: pomegranate");

        Assert.Equal(
            ["Budget-2026.final.pdf"],
            await workspace.FoundAsync("budg 2026", TestContext.Current.CancellationToken));

        // VISION.md draws the line here: what is in a file is never indexed.
        Assert.Empty(await workspace.FoundAsync("pomegranate", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task What_is_in_the_Trash_is_not_found()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        var page = await workspace.PageAsync(
            "Architecture", "Something.", TestContext.Current.CancellationToken);

        Assert.NotEmpty(await workspace.FoundAsync("architecture", TestContext.Current.CancellationToken));

        await workspace.DeleteAsync($"/api/knowledge/pages/{page}", TestContext.Current.CancellationToken);

        // The statements are hand-written, so this is the assertion that the
        // `deleted_at is null` in each of them is really there.
        Assert.Empty(await workspace.FoundAsync("architecture", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_switched_off_application_contributes_nothing_and_refuses_nothing()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync("Architecture", string.Empty, TestContext.Current.CancellationToken);
        await workspace.EntryAsync("architecture notes", TestContext.Current.CancellationToken);

        await SwitchedOffAsync(workspace, "knowledge");

        // An aggregate view leaves it out rather than refusing the request, the
        // way GET /api/trash does.
        var found = await workspace.SearchAsync("architecture", TestContext.Current.CancellationToken);

        Assert.Equal(
            ["scratchpad"],
            found["items"]!.AsArray().Select(item => item!["application"]!.GetValue<string>()));
    }

    [Fact]
    public async Task An_agent_finds_what_its_access_reaches_and_nothing_else()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync("Architecture", string.Empty, TestContext.Current.CancellationToken);
        await workspace.EntryAsync("architecture notes", TestContext.Current.CancellationToken);

        using var agent = await AnAgentAsync(workspace, knowledge: "read");

        var found = JsonNode.Parse(await agent.GetStringAsync(
            "/api/search?q=architecture", TestContext.Current.CancellationToken))!;

        Assert.Equal(
            ["knowledge"],
            found["items"]!.AsArray().Select(item => item!["application"]!.GetValue<string>()));
    }

    [Fact]
    public async Task An_agent_with_no_access_at_all_finds_nothing_and_is_not_refused()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync("Architecture", string.Empty, TestContext.Current.CancellationToken);

        using var agent = await AnAgentAsync(workspace, knowledge: "none");

        using var response = await agent.GetAsync(
            "/api/search?q=architecture", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var found = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Empty(found["items"]!.AsArray());
    }

    [Fact]
    public async Task Naming_one_application_narrows_the_answer_to_it()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync("Architecture", string.Empty, TestContext.Current.CancellationToken);
        await workspace.EntryAsync("architecture notes", TestContext.Current.CancellationToken);

        var found = JsonNode.Parse(await workspace.Owner.GetStringAsync(
            "/api/search?q=architecture&application=scratchpad",
            TestContext.Current.CancellationToken))!;

        Assert.Equal(
            ["scratchpad"],
            found["items"]!.AsArray().Select(item => item!["application"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_limit_says_out_loud_that_it_cut_something_off()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        for (var number = 0; number < 4; number++)
        {
            await workspace.EntryAsync(
                $"architecture note {number}", TestContext.Current.CancellationToken);
        }

        var found = JsonNode.Parse(await workspace.Owner.GetStringAsync(
            "/api/search?q=architecture&limit=2", TestContext.Current.CancellationToken))!;

        Assert.Equal(2, found["items"]!.AsArray().Count);
        Assert.True(found["has_more"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_found_thing_says_what_it_sits_in_so_a_client_can_open_the_screen_it_is_on()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        var list = await workspace.ListAsync("Work", TestContext.Current.CancellationToken);
        await workspace.TaskAsync(
            list, "Pomegranate", string.Empty, null, TestContext.Current.CancellationToken);

        var found = (await workspace.SearchAsync("pomegranate", TestContext.Current.CancellationToken))
            ["items"]!.AsArray();

        Assert.Equal(list, Assert.Single(found)!["within"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task A_snippet_quotes_the_body_and_marks_nothing_up()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync(
            "Notes",
            "The pomegranate is mentioned exactly once, here, in a sentence.",
            TestContext.Current.CancellationToken);

        var snippet = Assert.Single(
            (await workspace.SearchAsync("pomegranate", TestContext.Current.CancellationToken))
                ["items"]!.AsArray())!
            ["snippet"]!.GetValue<string>();

        Assert.Contains("pomegranate", snippet, StringComparison.Ordinal);
        Assert.DoesNotContain('<', snippet);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public async Task A_search_with_nothing_to_look_for_is_refused(string typed)
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        using var response = await workspace.Owner.GetAsync(
            $"/api/search?q={Uri.EscapeDataString(typed)}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nothing_a_caller_types_can_be_an_operator()
    {
        await using var workspace = await AWorkspace.StartedAsync(
            postgres, TestContext.Current.CancellationToken);

        await workspace.PageAsync("Architecture", string.Empty, TestContext.Current.CancellationToken);

        // Every character a tsquery reads as syntax, sent as the search itself.
        // The needle is taken apart before it reaches a statement, so this is a
        // search for one word and not a malformed query.
        Assert.Equal(
            ["Architecture"],
            await workspace.FoundAsync(
                "architecture & !x | (y:*) 'z' <-> \\", TestContext.Current.CancellationToken));
    }

    private static async Task SwitchedOffAsync(AWorkspace workspace, string application)
    {
        var state = JsonNode.Parse(await workspace.Owner.GetStringAsync(
            "/api/applications", TestContext.Current.CancellationToken))!
            ["items"]!.AsArray()
            .First(item => item!["application"]!.GetValue<string>() == application)!;

        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/applications/{application}")
        {
            Content = JsonContent.Create(new { enabled = false }),
        };

        // A list answers no ETag, so the version is the item's own updated_at —
        // which is what docs/api.md says a write holds when the read was a list.
        request.Headers.TryAddWithoutValidation(
            "If-Match",
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
