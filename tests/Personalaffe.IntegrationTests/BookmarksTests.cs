using System.Net;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarksTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_edits_have_one_winner_and_disabling_keeps_content()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var link = await links.Link("Original");
        var first = links.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}",
            new { title = "First", url = "https://example.com/first" }, link.Version);
        var second = links.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}",
            new { title = "Second", url = "https://example.com/second" }, link.Version);
        var answers = await Task.WhenAll(first, second);
        Assert.Single(answers, answer => answer.Status == HttpStatusCode.OK);
        Assert.Single(answers, answer => answer.Status == HttpStatusCode.PreconditionFailed);
        var apps = (await links.Send(HttpMethod.Get, "/api/applications")).Body!["items"]!.AsArray();
        var app = apps.Single(row => row!["application"]!.GetValue<string>() == "bookmarks")!;
        var version = '"' + app["updated_at"]!.GetValue<string>() + '"';
        var off = await links.Send(HttpMethod.Put, "/api/applications/bookmarks", new { enabled = false }, version);
        Assert.Equal(HttpStatusCode.OK, off.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await links.Send(HttpMethod.Get, "/api/bookmarks")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await links.Link("Refused")).Status);
        await links.Send(HttpMethod.Put, "/api/applications/bookmarks", new { enabled = true }, '"' + off.Body!["updated_at"]!.GetValue<string>() + '"');
        Assert.Equal(HttpStatusCode.OK, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}")).Status);
    }

    [Fact]
    public async Task Folders_are_paged_and_cycles_are_refused_without_changing_the_tree()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var root = await links.Folder("A");
        var child = await links.Folder("B", root.Id);
        var move = await links.Send(HttpMethod.Put, $"/api/bookmarks/folders/{root.Id}",
            new { name = "A", parent = child.Id, @private = false }, root.Version);
        Assert.Equal(HttpStatusCode.BadRequest, move.Status);
        var first = await links.Send(HttpMethod.Get, "/api/bookmarks/folders?limit=1");
        Assert.Equal(root.Id, first.Body!["items"]![0]!["id"]!.GetValue<Guid>());
        var second = await links.Send(HttpMethod.Get, "/api/bookmarks/folders?limit=1&offset=1");
        Assert.Equal(child.Id, second.Body!["items"]![0]!["id"]!.GetValue<Guid>());
        Assert.Null(second.Body["next_offset"]);
    }

    [Fact]
    public async Task Capture_move_stale_write_pagination_and_deletion_use_the_existing_contract()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var folder = await links.Folder("Reading");
        var one = await links.Link("One");
        var two = await links.Link("Two");
        Assert.Equal(HttpStatusCode.Created, one.Status);
        var changed = await links.Send(HttpMethod.Put, $"/api/bookmarks/{one.Id}",
            new { title = "Renamed", url = "https://example.com/changed", description = "Plain <b>text</b>", folder = folder.Id }, one.Version);
        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal(one.Id, changed.Id);
        var stale = await links.Send(HttpMethod.Put, $"/api/bookmarks/{one.Id}",
            new { title = "Lost edit", url = "https://example.com/", folder = (Guid?)null }, one.Version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        var first = await links.Send(HttpMethod.Get, "/api/bookmarks?limit=1");
        Assert.Equal(two.Id, first.Body!["items"]![0]!["id"]!.GetValue<Guid>());
        var next = first.Body["next_offset"]!.GetValue<int>();
        var second = await links.Send(HttpMethod.Get, $"/api/bookmarks?limit=1&offset={next}");
        Assert.Equal(one.Id, second.Body!["items"]![0]!["id"]!.GetValue<Guid>());
        Assert.Null(second.Body["next_offset"]);
        Assert.Equal(HttpStatusCode.NoContent, (await links.Send(HttpMethod.Delete, $"/api/bookmarks/{one.Id}", version: changed.Version)).Status);
        var deleted = await links.Send(HttpMethod.Get, $"/api/bookmarks/{one.Id}");
        Assert.Equal("deleted", deleted.Body!["type"]!.GetValue<string>().Split('/')[^1]);
    }

    [Fact]
    public async Task Private_context_is_required_on_each_read_write_and_trash_operation()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var parent = await links.Folder("Hidden", isPrivate: true, context: true);
        Assert.Equal(HttpStatusCode.Created, parent.Status);
        var child = await links.Folder("Child", parent.Id, context: true);
        var link = await links.Link("HiddenLink", child.Id, context: true);
        Assert.Equal(HttpStatusCode.Created, link.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await links.Send(HttpMethod.Delete, $"/api/bookmarks/{link.Id}", version: link.Version)).Status);
        Assert.Empty((await links.Send(HttpMethod.Get, "/api/bookmarks")).Body!["items"]!.AsArray());
        using var none = await links.Agent("none");
        using var read = await links.Agent("read");
        using var write = await links.Agent("read_write");
        Assert.Equal(HttpStatusCode.Forbidden, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}", context: true, client: none)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}", client: read)).Status);
        Assert.Equal(HttpStatusCode.OK, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}", context: true, client: read)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await links.Send(HttpMethod.Delete, $"/api/bookmarks/{link.Id}", version: link.Version, context: true, client: read)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await links.Send(HttpMethod.Delete, $"/api/bookmarks/folders/{parent.Id}", version: parent.Version, context: true, client: write)).Status);
        Assert.Empty((await links.Send(HttpMethod.Get, "/api/trash?application=bookmarks")).Body!["items"]!.AsArray());
        var trash = await links.Send(HttpMethod.Get, "/api/trash?application=bookmarks", context: true);
        Assert.Single(trash.Body!["items"]!.AsArray());
        Assert.Equal(parent.Id, trash.Body["items"]![0]!["id"]!.GetValue<Guid>());
        var version = '"' + trash.Body["items"]![0]!["updated_at"]!.GetValue<string>() + '"';
        Assert.Equal(HttpStatusCode.NotFound, (await links.Send(HttpMethod.Post, $"/api/trash/bookmarks/{parent.Id}/restore", new { }, version)).Status);
        Assert.Equal(HttpStatusCode.OK, (await links.Send(HttpMethod.Post, $"/api/trash/bookmarks/{parent.Id}/restore", new { }, version, true)).Status);
        Assert.Equal(HttpStatusCode.OK, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}", context: true)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}")).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_separate_deletion_survives_its_ancestors_removal_with_the_same_privacy(bool isPrivate)
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var folder = await links.Folder("Folder", isPrivate: isPrivate, context: isPrivate);
        var link = await links.Link("Separate", folder.Id, isPrivate);
        await links.Send(HttpMethod.Delete, $"/api/bookmarks/{link.Id}", version: link.Version, context: isPrivate);
        await links.Send(HttpMethod.Delete, $"/api/bookmarks/folders/{folder.Id}", version: folder.Version, context: isPrivate);
        var trash = (await links.Send(HttpMethod.Get, "/api/trash?application=bookmarks", context: isPrivate)).Body!["items"]!.AsArray();
        Assert.Equal(2, trash.Count);
        var root = trash.Single(row => row!["id"]!.GetValue<Guid>() == folder.Id)!;
        var version = '"' + root["updated_at"]!.GetValue<string>() + '"';
        Assert.Equal(HttpStatusCode.NoContent, (await links.Send(HttpMethod.Delete, $"/api/trash/bookmarks/{folder.Id}", version: version, context: isPrivate)).Status);
        var deleted = trash.Single(row => row!["id"]!.GetValue<Guid>() == link.Id)!;
        version = '"' + deleted["updated_at"]!.GetValue<string>() + '"';
        var restored = await links.Send(HttpMethod.Post, $"/api/trash/bookmarks/{link.Id}/restore", new { }, version, isPrivate);
        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.True(restored.Body!["moved_to_the_root"]!.GetValue<bool>());
        Assert.Equal(isPrivate ? HttpStatusCode.NotFound : HttpStatusCode.OK, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}")).Status);
    }

    [Fact]
    public async Task Restoring_a_separate_link_restores_only_its_needed_ancestors_and_agents_cannot_destroy_it()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var folder = await links.Folder("Private", isPrivate: true, context: true);
        var selected = await links.Link("Selected", folder.Id, true);
        var sibling = await links.Link("Sibling", folder.Id, true);
        await links.Send(HttpMethod.Delete, $"/api/bookmarks/{selected.Id}", version: selected.Version, context: true);
        await links.Send(HttpMethod.Delete, $"/api/bookmarks/folders/{folder.Id}", version: folder.Version, context: true);
        var trash = (await links.Send(HttpMethod.Get, "/api/trash?application=bookmarks", context: true)).Body!["items"]!.AsArray();
        var entry = trash.Single(row => row!["id"]!.GetValue<Guid>() == selected.Id)!;
        var version = '"' + entry["updated_at"]!.GetValue<string>() + '"';
        using var agent = await links.Agent("read_write");
        Assert.Equal(HttpStatusCode.Forbidden, (await links.Send(HttpMethod.Delete, $"/api/trash/bookmarks/{selected.Id}", version: version, context: true, client: agent)).Status);
        Assert.Equal(HttpStatusCode.OK, (await links.Send(HttpMethod.Post, $"/api/trash/bookmarks/{selected.Id}/restore", new { }, version, true, agent)).Status);
        Assert.Equal(HttpStatusCode.OK, (await links.Send(HttpMethod.Get, $"/api/bookmarks/folders/{folder.Id}", context: true)).Status);
        Assert.Equal(HttpStatusCode.OK, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{selected.Id}", context: true)).Status);
        Assert.Equal("/problems/deleted", (await links.Send(HttpMethod.Get, $"/api/bookmarks/{sibling.Id}", context: true)).Body!["type"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await links.Send(HttpMethod.Get, $"/api/bookmarks/{selected.Id}")).Status);
    }
}
