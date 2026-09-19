using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Personalaffe.Api.Http;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The one setting whose whole purpose is recognition: what this instance is
/// called, what its mark looks like, who may set it and who may read it.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AppearanceTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fresh_instance_is_unnamed_and_wears_what_the_product_always_wore()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var appearance = await ReadAsync(workspace);

        Assert.Null(appearance["title"]);
        Assert.Equal("violet", appearance["colour"]!.GetValue<string>());
        Assert.Equal("square", appearance["shape"]!.GetValue<string>());
    }

    [Fact]
    public async Task Naming_it_answers_what_it_is_now_called()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var appearance = await SetAsync(workspace, "Haus", "teal", "circle");

        Assert.Equal("Haus", appearance["title"]!.GetValue<string>());
        Assert.Equal("teal", appearance["colour"]!.GetValue<string>());
        Assert.Equal("circle", appearance["shape"]!.GetValue<string>());

        // And it is what the next reader sees, rather than what this writer was
        // told.
        Assert.Equal("Haus", (await ReadAsync(workspace))["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task It_is_readable_without_a_credential_and_nothing_else_about_the_owner_is()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        await SetAsync(workspace, "Haus", "violet", "square");

        using var stranger = workspace.Instance.CreateClient();

        using var read = await stranger.GetAsync("/api/appearance", Token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var appearance = JsonNode.Parse(await read.Content.ReadAsStringAsync(Token))!;
        Assert.Equal("Haus", appearance["title"]!.GetValue<string>());

        // The whole of what it says. Who the owner is stays behind the door,
        // and this is the list of fields that says so.
        Assert.Equal(
            ["colour", "shape", "title", "updated_at"],
            appearance.AsObject().Select(field => field.Key).Order(StringComparer.Ordinal));

        using var me = await stranger.GetAsync("/api/me", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task A_stranger_may_not_name_somebody_elses_instance()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var version = await workspace.VersionOfAsync("/api/appearance", Token);

        using var stranger = workspace.Instance.CreateClient();
        using var request = Write("Theirs", "red", "circle", version);
        using var response = await stranger.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_agent_reads_it_and_may_not_write_it()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        using var agent = await AnAgentAsync(workspace);

        using var read = await agent.GetAsync("/api/appearance", Token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // The write carries a version it really holds, so what it is refused
        // for is who is asking and nothing else.
        using var request = Write(
            "An agent's idea", "red", "circle",
            await workspace.VersionOfAsync("/api/appearance", Token));

        using var written = await agent.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Forbidden, written.StatusCode);
    }

    [Fact]
    public async Task A_write_holding_an_older_version_is_refused_as_stale()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var stale = await workspace.VersionOfAsync("/api/appearance", Token);

        await SetAsync(workspace, "Haus", "teal", "circle");

        using var request = Write("Somewhere else", "red", "square", stale);
        using var response = await workspace.Owner.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);

        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
        Assert.Equal("/problems/stale", problem["type"]!.GetValue<string>());

        // And nothing was half-written.
        Assert.Equal("Haus", (await ReadAsync(workspace))["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_write_with_no_version_at_all_is_refused()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        using var response = await workspace.Owner.PutAsJsonAsync(
            "/api/appearance",
            new { title = "Haus", colour = "violet", shape = "square" },
            Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task A_title_of_forty_one_characters_is_refused()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        using var accepted = await WriteAsync(workspace, new string('a', 40), "violet", "square");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var refused = await WriteAsync(workspace, new string('a', 41), "violet", "square");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_title_that_is_only_space_is_stored_as_none(string title)
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        await SetAsync(workspace, "Haus", "violet", "square");

        Assert.Null((await SetAsync(workspace, title, "violet", "square"))["title"]);
    }

    [Fact]
    public async Task A_title_is_kept_exactly_as_it_was_written()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        // Nothing on the way in escapes it, strips it or renders it. Every
        // reader is the one that has to write it out literally.
        const string Trouble = "<script>**x**</script>";

        Assert.Equal(
            Trouble, (await SetAsync(workspace, Trouble, "violet", "square"))["title"]!.GetValue<string>());
        Assert.Equal(
            Trouble, (await ReadAsync(workspace))["title"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("chartreuse", "square")]
    [InlineData("violet", "triangle")]
    public async Task A_colour_or_a_shape_that_is_not_in_the_set_is_refused(string colour, string shape)
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        using var response = await WriteAsync(workspace, "Haus", colour, shape);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await ReadAsync(workspace))["title"]);
    }

    [Fact]
    public async Task The_read_carries_the_version_a_write_has_to_hold()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        using var read = await workspace.Owner.GetAsync("/api/appearance", Token);
        var tag = read.Headers.ETag?.ToString();

        Assert.NotNull(tag);

        var appearance = JsonNode.Parse(await read.Content.ReadAsStringAsync(Token))!;

        // The header and the field say the same thing, so a client that read a
        // list can write without reading again.
        Assert.Equal($"\"{appearance["updated_at"]!.GetValue<string>()}\"", tag);
    }

    private static async Task<JsonNode> ReadAsync(AWorkspace workspace) =>
        JsonNode.Parse(await workspace.Owner.GetStringAsync("/api/appearance", Token))!;

    private static async Task<JsonNode> SetAsync(
        AWorkspace workspace, string? title, string colour, string shape)
    {
        using var response = await WriteAsync(workspace, title, colour, shape);

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
    }

    private static async Task<HttpResponseMessage> WriteAsync(
        AWorkspace workspace, string? title, string colour, string shape)
    {
        using var request = Write(
            title, colour, shape, await workspace.VersionOfAsync("/api/appearance", Token));

        return await workspace.Owner.SendAsync(request, Token);
    }

    private static HttpRequestMessage Write(
        string? title, string colour, string shape, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/appearance")
        {
            Content = JsonContent.Create(new { title, colour, shape }),
        };

        request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, version);

        return request;
    }

    private static async Task<HttpClient> AnAgentAsync(AWorkspace workspace)
    {
        using var granted = await workspace.Owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new
                {
                    scratchpad = "read_write",
                    knowledge = "read_write",
                    tasks = "read_write",
                    files = "read_write",
                },
            },
            Token);

        granted.EnsureSuccessStatusCode();

        var token = JsonNode.Parse(
            await granted.Content.ReadAsStringAsync(Token))!["token"]!.GetValue<string>();

        var client = workspace.Instance.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
