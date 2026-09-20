using System.Net;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarkTagTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Tags_normalize_filter_with_all_terms_search_and_survive_restore_without_private_leaks()
    {
        await using var books = await ABookmarkCollection.StartedAsync(postgres);
        var folder = await books.Folder("References");
        var secretFolder = await books.Folder("Hidden", isPrivate: true, context: true);
        var publicLink = await books.Send(HttpMethod.Post, "/api/bookmarks", new { title = "Guide", url = "https://example.com", folder = folder.Id, tags = new[] { " Work ", "WORK", "Research" } });
        Assert.Equal(HttpStatusCode.Created, publicLink.Status);
        Assert.Equal(new[] { "research", "work" }, publicLink.Body!["tags"]!.AsArray().Select(value => value!.GetValue<string>()));
        await books.Send(HttpMethod.Post, "/api/bookmarks", new { title = "Secret", url = "https://secret.example.com", folder = secretFolder.Id, tags = new[] { "private-only", "work" } }, context: true);
        var tags = await books.Send(HttpMethod.Get, "/api/bookmarks/tags");
        Assert.Equal(HttpStatusCode.OK, tags.Status);
        Assert.DoesNotContain("private-only", tags.Body!.ToJsonString());
        Assert.All(tags.Body.AsArray(), tag => Assert.Equal(1, tag!["count"]!.GetValue<int>()));
        Assert.Contains("private-only", (await books.Send(HttpMethod.Get, "/api/bookmarks/tags", context: true)).Body!.ToJsonString());
        Assert.Single((await books.Send(HttpMethod.Get, $"/api/bookmarks?tag=WORK&tag=research&folder={folder.Id}&q=gui")).Body!["items"]!.AsArray());
        Assert.Empty((await books.Send(HttpMethod.Get, "/api/bookmarks?tag=work&tag=missing")).Body!["items"]!.AsArray());
        Assert.Single((await books.Send(HttpMethod.Get, "/api/search?q=rese&application=bookmarks")).Body!["items"]!.AsArray());
        var updated = await books.Send(HttpMethod.Put, $"/api/bookmarks/{publicLink.Id}", new { title = "Guide", url = "https://example.com", folder = folder.Id, tags = new[] { "Updated" } }, publicLink.Version);
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await books.Send(HttpMethod.Put, $"/api/bookmarks/{publicLink.Id}", new { title = "Guide", url = "https://example.com", tags = Array.Empty<string>() }, publicLink.Version)).Status);
        Assert.Empty((await books.Send(HttpMethod.Get, "/api/search?q=rese&application=bookmarks")).Body!["items"]!.AsArray());
        await books.Send(HttpMethod.Delete, $"/api/bookmarks/{publicLink.Id}", version: updated.Version);
        var entry = (await books.Send(HttpMethod.Get, "/api/trash?application=bookmarks")).Body!["items"]![0]!;
        await books.Send(HttpMethod.Post, $"/api/trash/bookmarks/{publicLink.Id}/restore", version: $"\"{entry["updated_at"]!.GetValue<string>()}\"");
        Assert.Equal("updated", (await books.Send(HttpMethod.Get, $"/api/bookmarks/{publicLink.Id}")).Body!["tags"]![0]!.GetValue<string>());
    }
}
