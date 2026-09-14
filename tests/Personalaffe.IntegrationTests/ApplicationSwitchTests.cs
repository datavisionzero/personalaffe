using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The application switch end to end: what a fresh instance has switched on,
/// who may change it, and what a switched-off application does to everything
/// that reaches into it.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ApplicationSwitchTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Actor TheOwner = new() { Kind = CallerKind.Owner, Id = Guid.CreateVersion7() };

    [Fact]
    public async Task A_fresh_instance_has_all_four_switched_on()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var items = await Applications(owner);

        Assert.Equal(["files", "knowledge", "scratchpad", "tasks"], items.Select(Named).Order());
        Assert.All(items, item => Assert.True(item!["enabled"]!.GetValue<bool>()));

        // The owner's permission is everything, everywhere, and the switch says
        // so beside each one: a client drawing navigation asks once.
        Assert.All(items, item => Assert.Equal("read_write", item!["permission"]!.GetValue<string>()));
    }

    [Fact]
    public async Task The_owner_switches_one_off_and_back_on_again()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var off = await Switch(owner, WorkspaceApplication.Tasks, enabled: false, await VersionOf(owner, "tasks"));

        Assert.Equal(HttpStatusCode.OK, off.Status);
        Assert.False(off.Body!["enabled"]!.GetValue<bool>());

        Assert.False(await IsEnabled(owner, "tasks"));
        Assert.True(await IsEnabled(owner, "knowledge"));

        // The answer carries the version the write produced, so switching twice
        // in a row does not need a read in between.
        var on = await Switch(
            owner,
            WorkspaceApplication.Tasks,
            enabled: true,
            ContentVersion.Of(off.Body["updated_at"]!.GetValue<DateTimeOffset>()));

        Assert.Equal(HttpStatusCode.OK, on.Status);
        Assert.True(await IsEnabled(owner, "tasks"));
    }

    [Fact]
    public async Task The_switch_survives_a_restart()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using (var owner = await AnOwner.SignedInAsync(instance, Token))
        {
            await Switch(owner, WorkspaceApplication.Files, enabled: false, await VersionOf(owner, "files"));
        }

        await using var again = AnInstance.Against(instance.ConnectionString);
        using var signedIn = await AnOwner.SignInAgainAsync(again, Token);

        Assert.False(await IsEnabled(signedIn, "files"));
    }

    [Fact]
    public async Task An_agent_may_read_the_switch_and_may_not_touch_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);
        using var agent = await AnAgent(instance, owner, knowledge: "read_write", files: "read");

        var items = await Applications(agent);

        // All four, whatever it may reach: the set is in the contract, and the
        // permission beside each is what tells the agent why it was refused.
        Assert.Equal(4, items.Count);
        Assert.Equal("read_write", PermissionFor(items, "knowledge"));
        Assert.Equal("read", PermissionFor(items, "files"));
        Assert.Equal("none", PermissionFor(items, "tasks"));

        var refused = await Switch(
            agent, WorkspaceApplication.Knowledge, enabled: false, await VersionOf(owner, "knowledge"));

        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.True(await IsEnabled(owner, "knowledge"));
    }

    [Fact]
    public async Task A_switch_holding_an_older_version_is_refused_and_changes_nothing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var read = await VersionOf(owner, "scratchpad");

        var first = await Switch(owner, WorkspaceApplication.Scratchpad, enabled: false, read);
        Assert.Equal(HttpStatusCode.OK, first.Status);

        // The second browser is still holding what the first one replaced.
        var stale = await Switch(owner, WorkspaceApplication.Scratchpad, enabled: true, read);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal("/problems/stale", stale.Body!["type"]!.GetValue<string>());
        Assert.False(await IsEnabled(owner, "scratchpad"));
    }

    [Fact]
    public async Task A_switch_that_says_nothing_about_a_version_is_refused_the_same_way()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var refused = await Switch(owner, WorkspaceApplication.Files, enabled: false, held: null);

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.Status);
        Assert.True(await IsEnabled(owner, "files"));
    }

    [Fact]
    public async Task A_word_that_names_none_of_the_four_is_a_validation_refusal()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/applications/calendar")
        {
            Content = JsonContent.Create(new { enabled = false }),
        };
        request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, "\"2026-01-01T00:00:00Z\"");

        using var refused = await owner.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_switched_off_application_is_out_of_the_trash_and_refuses_what_reaches_into_it()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var files = new AFakeTrash(WorkspaceApplication.Files);

        await using var instance = await AnInstance.StartedWithAsync(postgres, services =>
        {
            services.AddSingleton<ITrash>(knowledge);
            services.AddSingleton<ITrash>(files);
        });

        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var set = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        files.Holding("invoice.pdf", DateTimeOffset.UtcNow, TheOwner);

        await Switch(owner, WorkspaceApplication.Knowledge, enabled: false, await VersionOf(owner, "knowledge"));

        // Out of the aggregate view, and out of it because it was never asked.
        var items = (await owner.GetFromJsonAsync<JsonNode>("/api/trash", Token))!["items"]!.AsArray();
        Assert.Equal(["invoice.pdf"], items.Select(item => item!["name"]!.GetValue<string>()));

        // And reaching into it directly is refused with the code that says why,
        // rather than with "nothing at that address".
        using var restore = new HttpRequestMessage(
            HttpMethod.Post, $"/api/trash/knowledge/{set.Id}/restore");
        restore.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch, EntityTags.For(ContentVersion.Of(set.UpdatedAt)));

        using var refused = await owner.SendAsync(restore, Token);
        var body = JsonNode.Parse(await refused.Content.ReadAsStringAsync(Token))!;

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("/problems/disabled", body["type"]!.GetValue<string>());
        Assert.Equal("knowledge", body["application"]!.GetValue<string>());
        Assert.Empty(knowledge.Restored);

        // Nothing was destroyed on the way: switching it back on finds it where
        // it was.
        await Switch(owner, WorkspaceApplication.Knowledge, enabled: true, await VersionOf(owner, "knowledge"));

        var back = (await owner.GetFromJsonAsync<JsonNode>("/api/trash", Token))!["items"]!.AsArray();
        Assert.Contains("architecture", back.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emptying_the_trash_leaves_a_switched_off_application_alone()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var files = new AFakeTrash(WorkspaceApplication.Files);

        await using var instance = await AnInstance.StartedWithAsync(postgres, services =>
        {
            services.AddSingleton<ITrash>(knowledge);
            services.AddSingleton<ITrash>(files);
        });

        using var owner = await AnOwner.SignedInAsync(instance, Token);

        knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        files.Holding("invoice.pdf", DateTimeOffset.UtcNow, TheOwner);

        await Switch(owner, WorkspaceApplication.Knowledge, enabled: false, await VersionOf(owner, "knowledge"));

        using var emptied = await owner.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/trash"), Token);

        var answered = JsonNode.Parse(await emptied.Content.ReadAsStringAsync(Token))!;

        Assert.Equal(1, answered["removed"]!.GetValue<int>());
        Assert.Equal(1, files.Emptied);
        Assert.Equal(0, knowledge.Emptied);

        // And asking for the switched-off one by name says why rather than
        // quietly removing nothing.
        using var refused = await owner.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/trash?application=knowledge"), Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(0, knowledge.Emptied);
    }

    private static async Task<JsonArray> Applications(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonNode>("/api/applications", Token))!["items"]!.AsArray();

    private static async Task<bool> IsEnabled(HttpClient client, string application) =>
        (await Applications(client))
            .First(item => Named(item) == application)!["enabled"]!
            .GetValue<bool>();

    private static async Task<ContentVersion> VersionOf(HttpClient client, string application) =>
        ContentVersion.Of((await Applications(client))
            .First(item => Named(item) == application)!["updated_at"]!
            .GetValue<DateTimeOffset>());

    private static async Task<(HttpStatusCode Status, JsonNode? Body)> Switch(
        HttpClient client, WorkspaceApplication application, bool enabled, ContentVersion? held)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/applications/{application.ToString().ToLowerInvariant()}")
        {
            Content = JsonContent.Create(new { enabled }),
        };

        if (held is not null)
        {
            request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, EntityTags.For(held));
        }

        using var response = await client.SendAsync(request, Token);

        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync(Token)));
    }

    private static async Task<HttpClient> AnAgent(
        AnInstance instance, HttpClient owner, string knowledge, string files)
    {
        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new { name = $"an agent {Guid.NewGuid():n}"[..20], permissions = new { knowledge, files } },
            Token);

        var token = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!["token"]!.GetValue<string>();
        var client = instance.CreateClient();

        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    private static string Named(JsonNode? item) => item!["application"]!.GetValue<string>();

    private static string PermissionFor(JsonArray items, string application) =>
        items.First(item => Named(item) == application)!["permission"]!.GetValue<string>();
}
