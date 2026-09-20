using System.Net;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarkSearchTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Words_match_title_description_url_and_current_folder_path_together()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var parent = await links.Folder("Travel");
        var child = await links.Folder("Summer", parent.Id);
        var link = await links.Link("Boarding", child.Id);
        foreach (var query in new[] { "trav board", "summ descr", "exam com", "https board", "board descr" })
        {
            var answer = await links.Send(HttpMethod.Get, "/api/bookmarks?q=" + Uri.EscapeDataString(query));
            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal(link.Id, Assert.Single(answer.Body!["items"]!.AsArray())!["id"]!.GetValue<Guid>());
        }
        var renamed = await links.Send(HttpMethod.Put, $"/api/bookmarks/folders/{parent.Id}",
            new { name = "Planning", parent = (Guid?)null, @private = false }, parent.Version);
        Assert.Equal(HttpStatusCode.OK, renamed.Status);
        Assert.Empty((await links.Send(HttpMethod.Get, "/api/bookmarks?q=trav")).Body!["items"]!.AsArray());
        Assert.Single((await links.Send(HttpMethod.Get, "/api/bookmarks?q=plan")).Body!["items"]!.AsArray());
        await links.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}",
            new { title = "Boarding", url = "https://example.com/", folder = (Guid?)null }, link.Version);
        Assert.Empty((await links.Send(HttpMethod.Get, "/api/bookmarks?q=plan")).Body!["items"]!.AsArray());
    }

    [Fact]
    public async Task Folder_filter_includes_descendants_and_combines_with_favorites_and_search()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var root = await links.Folder("Root");
        var child = await links.Folder("Child", root.Id);
        var favorite = await links.Link("Zebra", child.Id);
        await links.Link("Alpha", child.Id);
        await links.Link("Unsorted");
        await links.Send(HttpMethod.Put, $"/api/bookmarks/{favorite.Id}/favorite", new { favorite = true, after = (Guid?)null }, favorite.Version);
        var filtered = await links.Send(HttpMethod.Get, $"/api/bookmarks?folder={root.Id}&favorites=true&q=zeb");
        Assert.Equal(favorite.Id, Assert.Single(filtered.Body!["items"]!.AsArray())!["id"]!.GetValue<Guid>());
        var sorted = await links.Send(HttpMethod.Get, $"/api/bookmarks?folder={root.Id}&sort=title&limit=1");
        Assert.Equal("Alpha", sorted.Body!["items"]![0]!["title"]!.GetValue<string>());
        Assert.Equal(1, sorted.Body["next_offset"]!.GetValue<int>());
        Assert.Single((await links.Send(HttpMethod.Get, "/api/bookmarks?unsorted=true")).Body!["items"]!.AsArray());
    }

    [Fact]
    public async Task Global_search_and_local_search_hide_private_names_urls_and_counts_until_explicitly_requested()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var folder = await links.Folder("HiddenDomain");
        var link = await links.Link("SecretReference", folder.Id);
        var publicFound = await links.Send(HttpMethod.Get, "/api/search?q=secre");
        Assert.Equal(link.Id, Assert.Single(publicFound.Body!["items"]!.AsArray())!["id"]!.GetValue<Guid>());
        Assert.Equal("https://example.com/SecretReference", publicFound.Body["items"]![0]!["target_url"]!.GetValue<string>());
        await links.Send(HttpMethod.Put, $"/api/bookmarks/folders/{folder.Id}", new { name = "HiddenDomain", parent = (Guid?)null, @private = true }, folder.Version, true);
        foreach (var path in new[] { "/api/search?q=secre", "/api/search?q=hidd", "/api/bookmarks?q=exam&limit=1" })
        {
            var hidden = await links.Send(HttpMethod.Get, path);
            Assert.Empty(hidden.Body!["items"]!.AsArray());
            Assert.DoesNotContain("SecretReference", hidden.Body.ToJsonString(), StringComparison.Ordinal);
        }
        Assert.False((await links.Send(HttpMethod.Get, "/api/search?q=secre&limit=1")).Body!["has_more"]!.GetValue<bool>());
        using var reader = await links.Agent("read");
        using var none = await links.Agent("none");
        Assert.Single((await links.Send(HttpMethod.Get, "/api/search?q=secre", context: true, client: reader)).Body!["items"]!.AsArray());
        Assert.Empty((await links.Send(HttpMethod.Get, "/api/search?q=secre", context: true, client: none)).Body!["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await links.Send(HttpMethod.Get, $"/api/bookmarks?folder={folder.Id}")).Status);
        await links.Send(HttpMethod.Delete, $"/api/bookmarks/{link.Id}", version: link.Version, context: true);
        Assert.Empty((await links.Send(HttpMethod.Get, "/api/search?q=secre", context: true)).Body!["items"]!.AsArray());
    }
}
