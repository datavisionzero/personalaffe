using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;
namespace Personalaffe.UnitTests;
public sealed class BookmarkTagTests
{
    [Fact]
    public void Tags_are_normalized_bounded_and_versioned_only_when_the_set_changes()
    {
        var now = DateTimeOffset.UtcNow;
        var row = Bookmark.Make("Title", "https://example.com", null, null, now, [" Work ", "WORK", "Café"]);
        Assert.Equal(new[] { "café", "work" }, row.Tags);
        Assert.False(row.Change(row.Title, row.Url, null, null, now.AddSeconds(1), ["work", "café"]));
        Assert.True(row.Change(row.Title, row.Url, null, null, now.AddSeconds(1), []));
        Assert.Empty(row.Tags);
        Assert.Throws<Refusal>(() => BookmarkTags.Of([" "]));
        Assert.Throws<Refusal>(() => BookmarkTags.Of(["bad\ntag"]));
        Assert.Throws<Refusal>(() => BookmarkTags.Of([new string('x', 65)]));
        Assert.Throws<Refusal>(() => BookmarkTags.Of(Enumerable.Range(0, 33).Select(i => i.ToString())));
    }
}
