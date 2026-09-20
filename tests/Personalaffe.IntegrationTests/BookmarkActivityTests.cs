using System.Net;
using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarkActivityTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_explicit_unique_openings_count_and_they_do_not_stale_an_edit()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var link = await links.Link("ReadMe");
        await links.Send(HttpMethod.Get, $"/api/bookmarks/{link.Id}");
        var dashboard = await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard");
        Assert.Empty(dashboard.Body!["frequent"]!.AsArray());
        var eventId = Guid.NewGuid();
        var first = links.Send(HttpMethod.Post, $"/api/bookmarks/{link.Id}/open", new { event_id = eventId });
        var second = links.Send(HttpMethod.Post, $"/api/bookmarks/{link.Id}/open", new { event_id = eventId });
        var edit = links.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}", new { title = "Edited", url = "https://example.com/" }, link.Version);
        Assert.All(await Task.WhenAll(first, second), answer => Assert.Equal(HttpStatusCode.NoContent, answer.Status));
        Assert.Equal(HttpStatusCode.OK, (await edit).Status);
        await using var db = AnInstance.ContextFor(links.Instance.ConnectionString);
        Assert.Equal(1, (await db.BookmarkOpenDays.SingleAsync(Token)).Count);
        Assert.Single(await db.BookmarkOpenings.ToListAsync(Token));
        dashboard = await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard");
        Assert.Equal(link.Id, dashboard.Body!["frequent"]![0]!["id"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Favorites_keep_manual_order_and_openings_never_duplicate_them_in_frequent()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var one = await links.Link("One");
        var two = await links.Link("Two");
        var first = await links.Send(HttpMethod.Put, $"/api/bookmarks/{one.Id}/favorite", new { favorite = true, after = (Guid?)null }, one.Version);
        var second = await links.Send(HttpMethod.Put, $"/api/bookmarks/{two.Id}/favorite", new { favorite = true, after = one.Id }, two.Version);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        await links.Send(HttpMethod.Post, $"/api/bookmarks/{one.Id}/open", new { event_id = Guid.NewGuid() });
        var moved = await links.Send(HttpMethod.Put, $"/api/bookmarks/{two.Id}/favorite", new { favorite = true, after = (Guid?)null }, second.Version);
        Assert.Equal(HttpStatusCode.OK, moved.Status);
        var dashboard = (await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard")).Body!;
        Assert.Equal(new[] { two.Id, one.Id }, dashboard["favorites"]!.AsArray().Select(row => row!["id"]!.GetValue<Guid>()));
        Assert.Empty(dashboard["frequent"]!.AsArray());
        var unchanged = await links.Send(HttpMethod.Get, $"/api/bookmarks/{one.Id}");
        Assert.Equal(first.Version, unchanged.Version);
    }

    [Fact]
    public async Task Frequent_uses_thirty_utc_calendar_days_then_last_opening_then_id()
    {
        var now = new DateTimeOffset(2026, 9, 20, 0, 30, 0, TimeSpan.Zero);
        await using var links = await ABookmarkCollection.StartedAsync(postgres, new FixedClock(now));
        var one = await links.Link("One");
        var two = await links.Link("Two");
        var old = await links.Link("Old");
        await using (var db = AnInstance.ContextFor(links.Instance.ConnectionString))
        {
            db.BookmarkOpenDays.Add(BookmarkOpenDay.First(one.Id, now.AddHours(-1)));
            db.BookmarkOpenDays.Add(BookmarkOpenDay.First(two.Id, now));
            db.BookmarkOpenDays.Add(BookmarkOpenDay.First(one.Id, new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero)));
            var expired = BookmarkOpenDay.First(old.Id, new DateTimeOffset(2026, 8, 22, 1, 59, 59, TimeSpan.FromHours(2)));
            for (var i = 0; i < 10; i++) expired.Again(expired.LastOpenedAt);
            db.BookmarkOpenDays.Add(expired);
            await db.SaveChangesAsync(Token);
        }
        var dashboard = (await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard")).Body!;
        Assert.Equal(new[] { one.Id, two.Id }, dashboard["frequent"]!.AsArray().Select(row => row!["id"]!.GetValue<Guid>()));
        await links.Send(HttpMethod.Post, $"/api/bookmarks/{two.Id}/open", new { event_id = Guid.NewGuid() });
        dashboard = (await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard")).Body!;
        Assert.Equal(two.Id, dashboard["frequent"]![0]!["id"]!.GetValue<Guid>());
        await links.Send(HttpMethod.Post, $"/api/bookmarks/{one.Id}/open", new { event_id = Guid.NewGuid() });
        await links.Send(HttpMethod.Post, $"/api/bookmarks/{two.Id}/open", new { event_id = Guid.NewGuid() });
        dashboard = (await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard")).Body!;
        Assert.Equal(new[] { one.Id, two.Id }.Order(), dashboard["frequent"]!.AsArray().Select(row => row!["id"]!.GetValue<Guid>()));
    }

    [Fact]
    public async Task Repeated_moves_into_one_gap_renumber_when_floating_point_room_runs_out()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var top = await links.Link("Top");
        var one = await links.Link("One");
        var two = await links.Link("Two");
        await links.Send(HttpMethod.Put, $"/api/bookmarks/{top.Id}/favorite", new { favorite = true, after = (Guid?)null }, top.Version);
        await links.Send(HttpMethod.Put, $"/api/bookmarks/{one.Id}/favorite", new { favorite = true, after = top.Id }, one.Version);
        await links.Send(HttpMethod.Put, $"/api/bookmarks/{two.Id}/favorite", new { favorite = true, after = top.Id }, two.Version);
        for (var i = 0; i < 60; i++)
        {
            var id = i % 2 == 0 ? one.Id : two.Id;
            var current = await links.Send(HttpMethod.Get, $"/api/bookmarks/{id}");
            var moved = await links.Send(HttpMethod.Put, $"/api/bookmarks/{id}/favorite", new { favorite = true, after = top.Id }, current.Version);
            Assert.Equal(HttpStatusCode.OK, moved.Status);
        }
        var dashboard = (await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard")).Body!;
        Assert.Equal(new[] { top.Id, two.Id, one.Id }, dashboard["favorites"]!.AsArray().Select(row => row!["id"]!.GetValue<Guid>()));
        Assert.Equal(3, dashboard["favorites"]!.AsArray().Select(row => row!["favorite_position"]!.GetValue<double>()).Distinct().Count());
    }

    [Fact]
    public async Task Hidden_and_deleted_favorites_and_openings_never_enter_the_dashboard()
    {
        await using var links = await ABookmarkCollection.StartedAsync(postgres);
        var folder = await links.Folder("Private", isPrivate: true, context: true);
        var link = await links.Link("Hidden", folder.Id, true);
        Assert.Equal(HttpStatusCode.NotFound, (await links.Send(HttpMethod.Post, $"/api/bookmarks/{link.Id}/open", new { event_id = Guid.NewGuid() })).Status);
        await links.Send(HttpMethod.Post, $"/api/bookmarks/{link.Id}/open", new { event_id = Guid.NewGuid() }, context: true);
        var dashboard = (await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard")).Body!;
        Assert.Empty(dashboard["frequent"]!.AsArray());
        var pinned = await links.Send(HttpMethod.Put, $"/api/bookmarks/{link.Id}/favorite", new { favorite = true, after = (Guid?)null }, link.Version, true);
        dashboard = (await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard")).Body!;
        Assert.Empty(dashboard["favorites"]!.AsArray());
        await links.Send(HttpMethod.Delete, $"/api/bookmarks/{link.Id}", version: pinned.Version, context: true);
        dashboard = (await links.Send(HttpMethod.Get, "/api/bookmarks/dashboard", context: true)).Body!;
        Assert.Empty(dashboard["favorites"]!.AsArray());
        Assert.Empty(dashboard["frequent"]!.AsArray());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
