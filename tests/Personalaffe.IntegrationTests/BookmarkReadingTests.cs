using System.Net;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarkReadingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Reading_is_independent_of_opening_favorites_privacy_and_restore_and_can_be_undone()
    {
        await using var books = await ABookmarkCollection.StartedAsync(postgres);
        var folder = await books.Folder("Reading");
        var link = await books.Send(HttpMethod.Post, "/api/bookmarks", new { title = "Guide", url = "https://example.com/guide", folder = folder.Id, read_later = true, tags = new[] { "research" } });
        var second = await books.Send(HttpMethod.Post, "/api/bookmarks", new { title = "Second", url = "https://example.com/second", read_later = true });
        var privateFolder = await books.Folder("Private", isPrivate: true, context: true);
        await books.Send(HttpMethod.Post, "/api/bookmarks", new { title = "Secret", url = "https://example.com/private", folder = privateFolder.Id, read_later = true }, context: true);
        var dashboard = await books.Send(HttpMethod.Get, "/api/bookmarks/dashboard?limit=1");
        Assert.Equal(2, dashboard.Body!["read_later_count"]!.GetValue<int>());
        Assert.Equal(second.Id, dashboard.Body["read_later"]![0]!["id"]!.GetValue<Guid>());
        Assert.Equal(3, (await books.Send(HttpMethod.Get, "/api/bookmarks/dashboard", context: true)).Body!["read_later_count"]!.GetValue<int>());
        Assert.Single((await books.Send(HttpMethod.Get, $"/api/bookmarks?read_later=true&tag=research&folder={folder.Id}&q=gui")).Body!["items"]!.AsArray());
        await books.Send(HttpMethod.Post, $"/api/bookmarks/{link.Id}/open", new { event_id = Guid.NewGuid() });
        Assert.True((await books.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}")).Body!["read_later"]!.GetValue<bool>());
        var favorite = await books.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}/favorite", new { favorite = true }, link.Version);
        var read = await books.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}/reading", new { read_later = false }, favorite.Version);
        Assert.Equal(HttpStatusCode.OK, read.Status); Assert.False(read.Body!["read_later"]!.GetValue<bool>());
        Assert.True(read.Body["favorite"]!.GetValue<bool>()); Assert.Equal(folder.Id, read.Body["folder"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await books.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}/reading", new { read_later = true }, favorite.Version)).Status);
        var undone = await books.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}/reading", new { read_later = true, queued_at = link.Body!["read_later_at"]!.GetValue<string>() }, read.Version);
        Assert.Equal(link.Body["read_later_at"]!.GetValue<string>(), undone.Body!["read_later_at"]!.GetValue<string>());
        await books.Send(HttpMethod.Delete, $"/api/bookmarks/{link.Id}", version: undone.Version);
        var trash = (await books.Send(HttpMethod.Get, "/api/trash?application=bookmarks")).Body!["items"]![0]!;
        await books.Send(HttpMethod.Post, $"/api/trash/bookmarks/{link.Id}/restore", version: $"\"{trash["updated_at"]!.GetValue<string>()}\"");
        Assert.Equal(link.Body["read_later_at"]!.GetValue<string>(), (await books.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}")).Body!["read_later_at"]!.GetValue<string>());
    }
}
