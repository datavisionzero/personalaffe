using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Personalaffe.Api.Http;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The Scratchpad end to end, against a real Postgres: the five endpoints, the
/// permission matrix in all three shapes, the guard on every write, the switch,
/// and the one claim this application makes that no other does — that what is
/// deleted is gone.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ScratchpadTests(PostgresFixture postgres)
{
    private const string Entries = "/api/scratchpad/entries";

    /// <summary>
    /// A piece of somebody's text with everything in it that breaks first:
    /// characters outside ASCII, an emoji, a tab, and newlines in the middle.
    /// </summary>
    private const string Note = "Größe: 5 m²\n\n\tZeile zwei — 日本語 🙂\nund die dritte";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Text_captured_on_one_device_is_read_on_another_byte_for_byte()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var desk = await AnOwner.SignedInAsync(instance, Token);

        // The trailing newline a shell adds, and nothing else: one is dropped
        // and the newlines inside are the person's.
        var captured = await Capture(desk, Note + "\n");

        Assert.Equal(HttpStatusCode.Created, captured.Status);
        Assert.Equal(Note, captured.Body!["text"]!.GetValue<string>());
        Assert.Equal($"{Entries}/{captured.Body["id"]!.GetValue<Guid>()}", captured.Location);

        // The other device: a second browser of the same owner's, which is what
        // "cross-device" is from the instance's side.
        using var phone = await AnOwner.SignInAgainAsync(instance, Token);

        var read = await phone.GetFromJsonAsync<JsonNode>(Entries, Token);
        var item = read!["items"]!.AsArray().Single();

        Assert.Equal(Note, item!["text"]!.GetValue<string>());
        Assert.Equal(
            Encoding.UTF8.GetByteCount(Note),
            Encoding.UTF8.GetByteCount(item["text"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_capture_answers_the_version_it_produced_so_the_next_write_needs_no_read()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var captured = await Capture(owner, "the wifi password");

        Assert.NotNull(captured.ETag);

        var pinned = await Rewrite(
            owner, captured.Body!["id"]!.GetValue<Guid>(), "the wifi password", true, captured.ETag);

        Assert.Equal(HttpStatusCode.OK, pinned.Status);
        Assert.True(pinned.Body!["pinned"]!.GetValue<bool>());

        // Pinned, so there is no expiry at all — which is what pinning means on
        // the wire.
        Assert.Null(pinned.Body["expires_at"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public async Task Text_that_is_nothing_is_refused_as_validation(string text)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var refused = await Capture(owner, text);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("/problems/validation", refused.Body!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Text_over_the_limit_is_refused_and_the_message_names_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var refused = await Capture(owner, new string('x', (64 * 1024) + 1));

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Contains("65536", refused.Body!["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_field_the_entry_does_not_define_is_said_out_loud()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        using var refused = await owner.PostAsJsonAsync(
            Entries, new { text = "a note", pinnned = true }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("/problems/unknown-field", await TypeOf(refused));
    }

    [Fact]
    public async Task The_list_is_newest_first_bounded_by_its_limit_and_says_when_it_was_cut()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        foreach (var text in new[] { "first", "second", "third" })
        {
            Assert.Equal(HttpStatusCode.Created, (await Capture(owner, text)).Status);
        }

        var all = await owner.GetFromJsonAsync<JsonNode>(Entries, Token);

        Assert.Equal(
            ["third", "second", "first"],
            all!["items"]!.AsArray().Select(item => item!["text"]!.GetValue<string>()));
        Assert.False(all["has_more"]!.GetValue<bool>());

        var cut = await owner.GetFromJsonAsync<JsonNode>($"{Entries}?limit=2", Token);

        Assert.Equal(2, cut!["items"]!.AsArray().Count);
        Assert.True(cut["has_more"]!.GetValue<bool>());

        foreach (var outside in new[] { "0", "-1", "1001" })
        {
            using var refused = await owner.GetAsync($"{Entries}?limit={outside}", Token);

            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
    }

    [Fact]
    public async Task A_deleted_entry_is_gone_and_its_address_never_says_it_can_come_back()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var captured = await Capture(owner, "something temporary");
        var id = captured.Body!["id"]!.GetValue<Guid>();

        using var deleted = await Sent(owner, HttpMethod.Delete, $"{Entries}/{id}", captured.ETag);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // `deleted` is a code this application never answers: it means the owner
        // can have the thing back, and here nobody ever can.
        using var gone = await owner.GetAsync($"{Entries}/{id}", Token);

        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("/problems/not-found", await TypeOf(gone));

        // A second delete is not-found too, and there is nothing to guard it
        // with any more.
        using var again = await Sent(owner, HttpMethod.Delete, $"{Entries}/{id}", captured.ETag);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task However_many_entries_are_deleted_the_trash_stays_empty()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        for (var i = 0; i < 5; i++)
        {
            var captured = await Capture(owner, $"note {i}");

            using var deleted = await Sent(
                owner,
                HttpMethod.Delete,
                $"{Entries}/{captured.Body!["id"]!.GetValue<Guid>()}",
                captured.ETag);

            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        // The one application that deliberately does not inherit the Trash. The
        // day somebody wires it in by habit, a deletion the owner was told is
        // final stops being final — so this is asserted rather than remembered.
        var trash = await owner.GetFromJsonAsync<JsonNode>("/api/trash", Token);

        Assert.Empty(trash!["items"]!.AsArray());
        Assert.Empty(await owner.GetFromJsonAsync<JsonNode>(Entries, Token) is { } left
            ? left["items"]!.AsArray()
            : []);
    }

    [Fact]
    public async Task A_write_holding_an_older_version_is_refused_and_changes_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var captured = await Capture(owner, "what the desk wrote");
        var id = captured.Body!["id"]!.GetValue<Guid>();

        var won = await Rewrite(owner, id, "what the phone wrote", false, captured.ETag);
        Assert.Equal(HttpStatusCode.OK, won.Status);

        // The desk is still holding what the phone replaced.
        var stale = await Rewrite(owner, id, "what the desk wrote later", false, captured.ETag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal("/problems/stale", stale.Body!["type"]!.GetValue<string>());

        // And it carries the version that is there now, so a client can tell
        // "somebody got there first" from "I sent something malformed".
        Assert.Equal(
            won.Body!["updated_at"]!.GetValue<DateTimeOffset>(),
            stale.Body["updated_at"]!.GetValue<DateTimeOffset>());

        var read = await owner.GetFromJsonAsync<JsonNode>($"{Entries}/{id}", Token);
        Assert.Equal("what the phone wrote", read!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_stale_delete_destroys_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var captured = await Capture(owner, "not yet");
        var id = captured.Body!["id"]!.GetValue<Guid>();

        await Rewrite(owner, id, "changed since", false, captured.ETag);

        using var refused = await Sent(owner, HttpMethod.Delete, $"{Entries}/{id}", captured.ETag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);

        using var still = await owner.GetAsync($"{Entries}/{id}", Token);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task A_write_that_asks_for_what_is_already_there_does_not_move_the_version()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var captured = await Capture(owner, "the same thing");
        var id = captured.Body!["id"]!.GetValue<Guid>();

        var again = await Rewrite(owner, id, "the same thing", false, captured.ETag);

        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(
            captured.Body["updated_at"]!.GetValue<DateTimeOffset>(),
            again.Body!["updated_at"]!.GetValue<DateTimeOffset>());

        // The guard is still checked, though: a write that agreed with what is
        // stored is still a write somebody made from a stale screen.
        var stale = await Rewrite(
            owner, id, "the same thing", false, "\"2026-01-01T00:00:00.000000Z\"");

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
    }

    [Fact]
    public async Task An_agent_with_read_write_does_everything_including_the_deletion()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);
        using var agent = await AnAgentReaching(instance, owner, "read_write");

        var captured = await Capture(agent, "an agent wrote this");
        Assert.Equal(HttpStatusCode.Created, captured.Status);

        var id = captured.Body!["id"]!.GetValue<Guid>();

        Assert.Equal(HttpStatusCode.OK, (await Rewrite(agent, id, "and edited it", true, captured.ETag)).Status);

        var read = await agent.GetFromJsonAsync<JsonNode>($"{Entries}/{id}", Token);
        var held = EntityTags.For(ContentVersion.Of(read!["updated_at"]!.GetValue<DateTimeOffset>()));

        // The one destruction in this product agent access may make: read_write
        // has included deletion since PERSONAL-E2, and for the Scratchpad that
        // deletion is permanent.
        using var deleted = await Sent(agent, HttpMethod.Delete, $"{Entries}/{id}", held);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Empty((await owner.GetFromJsonAsync<JsonNode>("/api/trash", Token))!["items"]!.AsArray());
    }

    [Fact]
    public async Task An_agent_that_may_only_read_reads_and_changes_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);
        using var agent = await AnAgentReaching(instance, owner, "read");

        var captured = await Capture(owner, "the owner's note");
        var id = captured.Body!["id"]!.GetValue<Guid>();

        using var list = await agent.GetAsync(Entries, Token);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        using var one = await agent.GetAsync($"{Entries}/{id}", Token);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Capture(agent, "not yours to write")).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden, (await Rewrite(agent, id, "nor to change", false, captured.ETag)).Status);

        using var refused = await Sent(agent, HttpMethod.Delete, $"{Entries}/{id}", captured.ETag);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // And nothing moved.
        var still = await owner.GetFromJsonAsync<JsonNode>($"{Entries}/{id}", Token);
        Assert.Equal("the owner's note", still!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_agent_with_no_access_is_refused_on_all_five()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);
        using var agent = await AnAgentReaching(instance, owner, "none");

        var captured = await Capture(owner, "not this agent's business");
        var id = captured.Body!["id"]!.GetValue<Guid>();

        foreach (var (method, address) in new (HttpMethod, string)[]
        {
            (HttpMethod.Get, Entries),
            (HttpMethod.Get, $"{Entries}/{id}"),
            (HttpMethod.Post, Entries),
            (HttpMethod.Put, $"{Entries}/{id}"),
            (HttpMethod.Delete, $"{Entries}/{id}"),
        })
        {
            using var refused = await Sent(agent, method, address, captured.ETag, "{\"text\":\"x\"}");

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
    }

    [Fact]
    public async Task A_switched_off_scratchpad_refuses_every_operation_and_keeps_what_is_in_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var captured = await Capture(owner, "written while it was on");
        var id = captured.Body!["id"]!.GetValue<Guid>();

        await Switch(owner, enabled: false);

        foreach (var (method, address) in new (HttpMethod, string)[]
        {
            (HttpMethod.Get, Entries),
            (HttpMethod.Get, $"{Entries}/{id}"),
            (HttpMethod.Post, Entries),
            (HttpMethod.Put, $"{Entries}/{id}"),
            (HttpMethod.Delete, $"{Entries}/{id}"),
        })
        {
            using var refused = await Sent(owner, method, address, captured.ETag, "{\"text\":\"x\"}");

            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("/problems/disabled", await TypeOf(refused));
        }

        // Switching it off hides it and removes nothing.
        await Switch(owner, enabled: true);

        var back = await owner.GetFromJsonAsync<JsonNode>($"{Entries}/{id}", Token);
        Assert.Equal("written while it was on", back!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Access_is_decided_before_the_switch()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);
        using var agent = await AnAgentReaching(instance, owner, "none");

        await Switch(owner, enabled: false);

        // A caller who may not reach an application learns nothing about how
        // the owner has configured their workspace (ADR 0004).
        using var refused = await agent.GetAsync(Entries, Token);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("/problems/forbidden", await TypeOf(refused));
    }

    [Fact]
    public async Task Nothing_of_what_the_owner_wrote_reaches_the_log()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        const string secret = "a-passphrase-that-must-never-be-logged";

        Assert.Equal(HttpStatusCode.Created, (await Capture(owner, secret)).Status);

        Assert.DoesNotContain(
            instance.Logged, line => line.Contains(secret, StringComparison.Ordinal));
    }

    private static async Task<(HttpStatusCode Status, JsonNode? Body, string? ETag, string? Location)> Capture(
        HttpClient client, string text)
    {
        using var response = await client.PostAsJsonAsync(Entries, new { text, pinned = false }, Token);

        return (
            response.StatusCode,
            JsonNode.Parse(await response.Content.ReadAsStringAsync(Token)),
            response.Headers.ETag?.ToString(),
            response.Headers.Location?.ToString());
    }

    private static async Task<(HttpStatusCode Status, JsonNode? Body)> Rewrite(
        HttpClient client, Guid id, string text, bool pinned, string? held)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Entries}/{id}")
        {
            Content = JsonContent.Create(new { text, pinned }),
        };

        if (held is not null)
        {
            request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, held);
        }

        using var response = await client.SendAsync(request, Token);

        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync(Token)));
    }

    private static async Task<HttpResponseMessage> Sent(
        HttpClient client, HttpMethod method, string address, string? held, string body = "{}")
    {
        using var request = new HttpRequestMessage(method, address);

        if (method != HttpMethod.Get)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (held is not null)
        {
            request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, held);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task Switch(HttpClient owner, bool enabled)
    {
        var applications = (await owner.GetFromJsonAsync<JsonNode>("/api/applications", Token))!["items"]!
            .AsArray();

        var state = applications.First(
            item => item!["application"]!.GetValue<string>() == "scratchpad")!;

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/applications/scratchpad")
        {
            Content = JsonContent.Create(new { enabled }),
        };

        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch,
            EntityTags.For(ContentVersion.Of(state["updated_at"]!.GetValue<DateTimeOffset>())));

        using var response = await owner.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpClient> AnAgentReaching(
        AnInstance instance, HttpClient owner, string scratchpad)
    {
        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new { name = $"an agent {Guid.NewGuid():n}"[..20], permissions = new { scratchpad } },
            Token);

        var token = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!["token"]!.GetValue<string>();
        var client = instance.CreateClient();

        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    private static async Task<string> TypeOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["type"]!.GetValue<string>();
}
