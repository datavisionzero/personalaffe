using System.Net;
namespace Personalaffe.IntegrationTests;
[Collection(nameof(PostgresCollection))]
public sealed class BookmarkDuplicateTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Groups_only_count_visible_exact_matches_and_atomic_cleanup_keeps_chosen_metadata()
    {
        await using var books = await ABookmarkCollection.StartedAsync(postgres);
        async Task<ABookmarkCollection.Answer> Add(string title, string url, Guid? folder = null, bool context = false) =>
            await books.Send(HttpMethod.Post, "/api/bookmarks", new { title, url, description = title, folder, tags = new[] { title }, read_later = true }, context: context);
        var kept = await Add("Keep", "HTTPS://EXAMPLE.COM:443/Path?a=1&b=2#fragment");
        var removed = await Add("Remove", "https://example.com/Path?a=1&b=2#fragment");
        var another = await Add("Another", "https://example.com/Path?a=1&b=2#fragment");
        await Add("Different", "https://example.com/path?a=1&b=2#fragment");
        var folder = await books.Folder("Private", isPrivate: true, context: true);
        var hidden = await Add("Hidden", "https://example.com/Path?a=1&b=2#fragment", folder.Id, true);
        var groups = await books.Send(HttpMethod.Get, "/api/bookmarks/duplicates?limit=1");
        Assert.Equal(HttpStatusCode.OK, groups.Status);
        Assert.Single(groups.Body!["groups"]!.AsArray());
        Assert.Equal(3, groups.Body["groups"]![0]!["count"]!.GetValue<int>());
        Assert.DoesNotContain("Hidden", groups.Body.ToJsonString());
        Assert.Equal(4, (await books.Send(HttpMethod.Get, "/api/bookmarks/duplicates", context: true)).Body!["groups"]![0]!["count"]!.GetValue<int>());
        object Pick(ABookmarkCollection.Answer row) => new { id = row.Id, updated_at = row.Body!["updated_at"]!.GetValue<string>() };
        var hiddenCleanup = await books.Send(HttpMethod.Post, "/api/bookmarks/duplicates/cleanup", new { keep = kept.Id, remove = new[] { Pick(removed), Pick(hidden) } }, kept.Version);
        Assert.Equal(HttpStatusCode.NotFound, hiddenCleanup.Status);
        Assert.Equal(HttpStatusCode.OK, (await books.Send(HttpMethod.Get, $"/api/bookmarks/{removed.Id}")).Status);
        var changed = await books.Send(HttpMethod.Put, $"/api/bookmarks/{another.Id}/favorite", new { favorite = true }, another.Version);
        var stale = await books.Send(HttpMethod.Post, "/api/bookmarks/duplicates/cleanup", new { keep = kept.Id, remove = new[] { Pick(removed), Pick(another) } }, kept.Version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal(HttpStatusCode.OK, (await books.Send(HttpMethod.Get, $"/api/bookmarks/{removed.Id}")).Status);
        var cleaned = await books.Send(HttpMethod.Post, "/api/bookmarks/duplicates/cleanup", new { keep = kept.Id, remove = new[] { Pick(removed), Pick(changed) } }, kept.Version);
        Assert.Equal(HttpStatusCode.OK, cleaned.Status);
        Assert.Equal(kept.Body!.ToJsonString(), (await books.Send(HttpMethod.Get, $"/api/bookmarks/{kept.Id}")).Body!.ToJsonString());
        Assert.Empty((await books.Send(HttpMethod.Get, "/api/bookmarks/duplicates")).Body!["groups"]!.AsArray());
        var trash = (await books.Send(HttpMethod.Get, "/api/trash?application=bookmarks")).Body!["items"]!.AsArray().Single(item => item!["id"]!.GetValue<Guid>() == removed.Id)!;
        await books.Send(HttpMethod.Post, $"/api/trash/bookmarks/{removed.Id}/restore", version: $"\"{trash["updated_at"]!.GetValue<string>()}\"");
        var restored = await books.Send(HttpMethod.Get, $"/api/bookmarks/{removed.Id}");
        Assert.Equal("remove", restored.Body!["tags"]![0]!.GetValue<string>());
        Assert.True(restored.Body["read_later"]!.GetValue<bool>());
        Assert.Single((await books.Send(HttpMethod.Get, "/api/bookmarks/duplicates")).Body!["groups"]!.AsArray());
    }

    [Fact]
    public async Task Duplicate_pages_are_bounded_and_keeper_changes_refuse_the_whole_selection()
    {
        await using var books = await ABookmarkCollection.StartedAsync(postgres);
        var copies = new List<ABookmarkCollection.Answer>();
        for (var i = 0; i < 52; i++) copies.Add(await books.Send(HttpMethod.Post, "/api/bookmarks", new { title = $"Copy {i}", url = "https://example.com/a" }));
        for (var i = 0; i < 2; i++) await books.Send(HttpMethod.Post, "/api/bookmarks", new { title = $"Other {i}", url = "https://example.com/b" });
        var first = (await books.Send(HttpMethod.Get, "/api/bookmarks/duplicates?limit=1")).Body!;
        Assert.Equal(1, first["next_offset"]!.GetValue<int>());
        Assert.Equal(52, first["groups"]![0]!["count"]!.GetValue<int>());
        Assert.Equal(50, first["groups"]![0]!["items"]!.AsArray().Count);
        Assert.Equal(50, first["groups"]![0]!["next_member_offset"]!.GetValue<int>());
        var last = (await books.Send(HttpMethod.Get, "/api/bookmarks/duplicates?url=https%3A%2F%2Fexample.com%2Fa&member_offset=50")).Body!;
        Assert.Equal(2, last["groups"]![0]!["items"]!.AsArray().Count);
        Assert.Null(last["groups"]![0]!["next_member_offset"]);
        await books.Send(HttpMethod.Put, $"/api/bookmarks/{copies[0].Id}/favorite", new { favorite = true }, copies[0].Version);
        var refused = await books.Send(HttpMethod.Post, "/api/bookmarks/duplicates/cleanup", new { keep = copies[0].Id, remove = new[] { new { id = copies[1].Id, updated_at = copies[1].Body!["updated_at"]!.GetValue<string>() } } }, copies[0].Version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.Status);
        Assert.Equal(HttpStatusCode.OK, (await books.Send(HttpMethod.Get, $"/api/bookmarks/{copies[1].Id}")).Status);
    }

    [Theory]
    [InlineData("https://example.com/path", "http://example.com/path")]
    [InlineData("https://example.com/Path", "https://example.com/path")]
    [InlineData("https://example.com/?a=1&b=2", "https://example.com/?b=2&a=1")]
    [InlineData("https://example.com/#one", "https://example.com/#two")]
    [InlineData("https://example.com/%7e", "https://example.com/~")]
    public void Conservative_comparison_preserves_intentional_differences(string first, string second) =>
        Assert.NotEqual(Domain.Bookmarks.BookmarkUrl.Key(first), Domain.Bookmarks.BookmarkUrl.Key(second));
}
