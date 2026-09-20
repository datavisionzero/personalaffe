using System.Net;
using System.Text.Json.Nodes;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarkHomeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Home_limits_visible_links_prioritizes_favorites_and_obeys_tile_permission_and_switch()
    {
        await using var books = await ABookmarkCollection.StartedAsync(postgres);
        var privateFolder = await books.Folder("Private", isPrivate: true, context: true);
        var secret = await books.Link("Secret", privateFolder.Id, context: true);
        await books.Send(HttpMethod.Put, $"/api/bookmarks/{secret.Id}/favorite", new { favorite = true }, secret.Version, context: true);
        var favorite = await books.Link("Favorite");
        await books.Send(HttpMethod.Put, $"/api/bookmarks/{favorite.Id}/favorite", new { favorite = true }, favorite.Version);
        for (var i = 0; i < 7; i++)
        {
            var link = await books.Link($"Frequent-{i}");
            await books.Send(HttpMethod.Post, $"/api/bookmarks/{link.Id}/open", new { event_id = Guid.NewGuid() });
        }
        var home = await books.Send(HttpMethod.Get, "/api/dashboard");
        Assert.Equal(HttpStatusCode.OK, home.Status);
        var rows = home.Body!["bookmarks"]!.AsArray();
        Assert.Equal(5, rows.Count);
        Assert.Equal(favorite.Id, rows[0]!["id"]!.GetValue<Guid>());
        Assert.Equal(5, rows.Select(row => row!["id"]!.GetValue<Guid>()).Distinct().Count());
        Assert.DoesNotContain("Secret", home.Body.ToJsonString());
        var privateHome = await books.Send(HttpMethod.Get, "/api/dashboard", context: true);
        Assert.Contains("Secret", privateHome.Body!.ToJsonString());
        using var denied = await books.Agent("none");
        Assert.Null((await books.Send(HttpMethod.Get, "/api/dashboard", context: true, client: denied)).Body!["bookmarks"]);
        var tile = home.Body["tiles"]!.AsArray().Single(row => row!["tile"]!.GetValue<string>() == "bookmarks")!;
        var hidden = await books.Send(HttpMethod.Put, "/api/dashboard/tiles/bookmarks", new { shown = false }, $"\"{tile["updated_at"]!.GetValue<string>()}\"");
        Assert.Equal(HttpStatusCode.OK, hidden.Status);
        Assert.Null((await books.Send(HttpMethod.Get, "/api/dashboard", context: true)).Body!["bookmarks"]);
        await books.Send(HttpMethod.Put, "/api/dashboard/tiles/bookmarks", new { shown = true }, hidden.Version);
        var applications = await books.Send(HttpMethod.Get, "/api/applications");
        var app = applications.Body!["items"]!.AsArray().Single(row => row!["application"]!.GetValue<string>() == "bookmarks")!;
        await books.Send(HttpMethod.Put, "/api/applications/bookmarks", new { enabled = false }, $"\"{app["updated_at"]!.GetValue<string>()}\"");
        Assert.Null((await books.Send(HttpMethod.Get, "/api/dashboard", context: true)).Body!["bookmarks"]);
    }
}
