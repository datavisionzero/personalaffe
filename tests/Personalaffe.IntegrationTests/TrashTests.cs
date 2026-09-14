using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The Trash end to end: one list over the applications, who may read it, who
/// may restore from it, and who may destroy anything.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TrashTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Actor TheOwner = new() { Kind = CallerKind.Owner, Id = Guid.CreateVersion7() };

    [Fact]
    public async Task An_instance_with_no_content_modules_answers_an_empty_trash()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var trash = await owner.GetFromJsonAsync<JsonNode>("/api/trash", Token);

        Assert.Empty(trash!["items"]!.AsArray());
        Assert.False(trash["has_more"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_owner_sees_every_application_newest_deletion_first()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var files = new AFakeTrash(WorkspaceApplication.Files);

        await using var instance = await Holding(knowledge, files);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var now = DateTimeOffset.UtcNow;
        knowledge.Holding("architecture", now.AddMinutes(-10), TheOwner, where: "/notes");
        files.Holding("invoice.pdf", now.AddMinutes(-1), TheOwner);
        knowledge.Holding("groceries", now.AddMinutes(-30), TheOwner);

        var items = (await owner.GetFromJsonAsync<JsonNode>("/api/trash", Token))!["items"]!.AsArray();

        Assert.Equal(3, items.Count);
        Assert.Equal(["invoice.pdf", "architecture", "groceries"], items.Select(Name));
        Assert.Equal(["files", "knowledge", "knowledge"], items.Select(Application));
        Assert.Equal("/notes", items[1]!["where"]!.GetValue<string>());
        Assert.Equal("owner", items[0]!["deleted_by"]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task One_application_can_be_asked_for_on_its_own()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var files = new AFakeTrash(WorkspaceApplication.Files);

        await using var instance = await Holding(knowledge, files);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        files.Holding("invoice.pdf", DateTimeOffset.UtcNow, TheOwner);

        var items = (await owner.GetFromJsonAsync<JsonNode>(
            "/api/trash?application=knowledge", Token))!["items"]!.AsArray();

        Assert.Equal(["architecture"], items.Select(Name));
    }

    [Fact]
    public async Task An_agent_sees_only_the_applications_it_may_read()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var files = new AFakeTrash(WorkspaceApplication.Files);

        await using var instance = await Holding(knowledge, files);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        files.Holding("invoice.pdf", DateTimeOffset.UtcNow, TheOwner);

        using var agent = await AnAgent(instance, owner, knowledge: "read", files: "none");

        var items = (await agent.GetFromJsonAsync<JsonNode>("/api/trash", Token))!["items"]!.AsArray();

        // Not filtered out of the list after the fact: the application it
        // cannot read was never asked.
        Assert.Equal(["architecture"], items.Select(Name));
        Assert.DoesNotContain("invoice.pdf", items.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restore_sends_back_the_version_the_list_gave_it()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var entry = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);

        using var restored = await owner.SendAsync(
            Restoring(entry, ContentVersion.Of(entry.UpdatedAt)), Token);

        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal([(entry.Id, null)], knowledge.Restored);

        // The answer says where it landed, because the place something came
        // from can be gone and nothing here moves content silently.
        var landed = JsonNode.Parse(await restored.Content.ReadAsStringAsync(Token))!;

        Assert.Equal("architecture", landed["name"]!.GetValue<string>());
        Assert.False(landed["moved_to_the_root"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_restore_holding_an_older_version_is_refused_and_restores_nothing()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var entry = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        var older = ContentVersion.Of(entry.UpdatedAt.AddMinutes(-5));

        using var refused = await owner.SendAsync(Restoring(entry, older), Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        Assert.Equal("/problems/stale", await CodeOf(refused));
        Assert.Empty(knowledge.Restored);
    }

    [Fact]
    public async Task A_restore_that_says_nothing_about_a_version_is_refused_the_same_way()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var entry = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);

        using var refused = await owner.SendAsync(Restoring(entry, held: null), Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        Assert.Empty(knowledge.Restored);
    }

    [Fact]
    public async Task An_agent_with_write_access_restores_and_one_with_read_access_does_not()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var reader = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);

        using var readOnly = await AnAgent(instance, owner, knowledge: "read", files: "none");
        using var refused = await readOnly.SendAsync(
            Restoring(reader, ContentVersion.Of(reader.UpdatedAt)), Token);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Empty(knowledge.Restored);

        using var writer = await AnAgent(instance, owner, knowledge: "read_write", files: "none");
        using var allowed = await writer.SendAsync(
            Restoring(reader, ContentVersion.Of(reader.UpdatedAt)), Token);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal([(reader.Id, null)], knowledge.Restored);
    }

    [Fact]
    public async Task Removing_something_for_good_is_the_owners_alone()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var entry = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        var version = ContentVersion.Of(entry.UpdatedAt);

        // Everything an agent can be given, which is still not this: an agent
        // that could destroy one entry could bypass the Trash in two steps.
        using var agent = await AnAgent(instance, owner, knowledge: "read_write", files: "read_write");

        using var refused = await agent.SendAsync(Removing(entry, version), Token);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var emptyRefused = await agent.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/trash"), Token);
        Assert.Equal(HttpStatusCode.Forbidden, emptyRefused.StatusCode);

        Assert.Empty(knowledge.Removed);

        using var removed = await owner.SendAsync(Removing(entry, version), Token);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal([entry.Id], knowledge.Removed);
    }

    [Fact]
    public async Task Emptying_it_removes_everything_the_list_showed()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var files = new AFakeTrash(WorkspaceApplication.Files);

        await using var instance = await Holding(knowledge, files);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        knowledge.Holding("groceries", DateTimeOffset.UtcNow, TheOwner);
        files.Holding("invoice.pdf", DateTimeOffset.UtcNow, TheOwner);

        using var emptied = await owner.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/trash?application=knowledge"), Token);

        var answered = JsonNode.Parse(await emptied.Content.ReadAsStringAsync(Token))!;

        Assert.Equal(2, answered["removed"]!.GetValue<int>());
        Assert.Equal(2, knowledge.Emptied);
        Assert.Equal(0, files.Emptied);

        var left = (await owner.GetFromJsonAsync<JsonNode>("/api/trash", Token))!["items"]!.AsArray();
        Assert.Equal(["invoice.pdf"], left.Select(Name));
    }

    [Fact]
    public async Task An_entry_that_is_not_there_is_a_plain_not_found()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var gone = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        var version = ContentVersion.Of(gone.UpdatedAt);

        using var first = await owner.SendAsync(Restoring(gone, version), Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await owner.SendAsync(Restoring(gone, version), Token);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal("/problems/not-found", await CodeOf(second));

        // An application with no module at all answers the same sentence: which
        // modules this build has is not something the Trash announces.
        var nothing = new TrashEntry(
            WorkspaceApplication.Tasks, Guid.CreateVersion7(), "a task", null,
            DateTimeOffset.UtcNow, TheOwner, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        using var third = await owner.SendAsync(Restoring(nothing, version), Token);
        Assert.Equal(HttpStatusCode.NotFound, third.StatusCode);
    }

    [Fact]
    public async Task A_trash_entry_still_names_the_agent_access_after_it_has_been_revoked()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new { name = "the laptop agent", permissions = new { knowledge = "read_write" } },
            Token);
        var agent = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!["agent"]!;
        granted.Dispose();

        knowledge.Holding(
            "architecture",
            DateTimeOffset.UtcNow,
            new Actor
            {
                Kind = CallerKind.Agent,
                Id = agent["id"]!.GetValue<Guid>(),
                Name = agent["name"]!.GetValue<string>(),
            });

        using var revoked = await owner.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/agents/{agent["id"]!.GetValue<Guid>()}"), Token);
        Assert.True(revoked.IsSuccessStatusCode);

        var items = (await owner.GetFromJsonAsync<JsonNode>("/api/trash", Token))!["items"]!.AsArray();

        Assert.Equal("the laptop agent", items[0]!["deleted_by"]!["name"]!.GetValue<string>());
        Assert.Equal("agent", items[0]!["deleted_by"]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_limit_bounds_the_answer_and_says_there_is_more()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var now = DateTimeOffset.UtcNow;
        for (var each = 0; each < 5; each++)
        {
            knowledge.Holding($"page {each}", now.AddMinutes(-each), TheOwner);
        }

        var limited = (await owner.GetFromJsonAsync<JsonNode>("/api/trash?limit=2", Token))!;

        Assert.Equal(2, limited["items"]!.AsArray().Count);
        Assert.True(limited["has_more"]!.GetValue<bool>());

        var whole = (await owner.GetFromJsonAsync<JsonNode>("/api/trash?limit=5", Token))!;

        Assert.Equal(5, whole["items"]!.AsArray().Count);
        Assert.False(whole["has_more"]!.GetValue<bool>());

        using var refused = await owner.GetAsync("/api/trash?limit=0", Token);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    private Task<AnInstance> Holding(params AFakeTrash[] contributors) =>
        AnInstance.StartedWithTrashAsync(postgres, [.. contributors]);

    private static async Task<HttpClient> AnAgent(
        AnInstance instance, HttpClient owner, string knowledge, string files)
    {
        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = $"an agent {Guid.NewGuid():n}"[..20],
                permissions = new { knowledge, files },
            },
            Token);

        var token = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!["token"]!.GetValue<string>();
        var client = instance.CreateClient();

        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    private static HttpRequestMessage Restoring(TrashEntry entry, ContentVersion? held) =>
        Guarded(
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/trash/{entry.Application.ToString().ToLowerInvariant()}/{entry.Id}/restore"),
            held);

    private static HttpRequestMessage Removing(TrashEntry entry, ContentVersion held) =>
        Guarded(
            new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/trash/{entry.Application.ToString().ToLowerInvariant()}/{entry.Id}"),
            held);

    private static HttpRequestMessage Guarded(HttpRequestMessage request, ContentVersion? held)
    {
        if (held is not null)
        {
            request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, EntityTags.For(held));
        }

        return request;
    }

    private static string Name(JsonNode? entry) => entry!["name"]!.GetValue<string>();

    private static string Application(JsonNode? entry) => entry!["application"]!.GetValue<string>();

    private static async Task<string> CodeOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["type"]!.GetValue<string>();
}
