using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.UnitTests;

public sealed class BookmarkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative")]
    [InlineData("https://")]
    [InlineData("https://example.com/\npath")]
    [InlineData("https:\\example.com")]
    public void Only_absolute_http_addresses_are_saved(string url) =>
        Assert.Throws<Refusal>(() => Bookmark.Make("A link", url, null, null, Now));

    [Fact]
    public void Edits_preserve_identity_and_plain_text_and_favorite_order()
    {
        var link = Bookmark.Make("<b>A link</b>", "https://example.com/a?q=1#part", "<script>text</script>", null, Now);
        var id = link.Id;
        link.FavoriteAt(12.5, Now.AddSeconds(1));
        var folder = Guid.NewGuid();
        Assert.True(link.Change("New title", "http://localhost:8080/", link.Description, folder, Now.AddSeconds(2)));
        Assert.Equal(id, link.Id);
        Assert.Equal(folder, link.FolderId);
        Assert.Equal(12.5, link.FavoritePosition);
        Assert.Equal("<script>text</script>", link.Description);
        Assert.False(link.Change(link.Title, link.Url, link.Description, folder, Now.AddSeconds(3)));
        Assert.Equal(Now.AddSeconds(2), link.UpdatedAt);
    }

    [Fact]
    public void Field_limits_and_positions_are_enforced()
    {
        Assert.Throws<Refusal>(() => Bookmark.Make(" ", "https://example.com", null, null, Now));
        Assert.Throws<Refusal>(() => Bookmark.Make(new string('x', 201), "https://example.com", null, null, Now));
        Assert.Throws<Refusal>(() => Bookmark.Make("Title", "https://example.com/" + new string('x', 8192), null, null, Now));
        Assert.Throws<Refusal>(() => Bookmark.Make("Title", "https://example.com", new string('ä', 32769), null, Now));
        var link = Bookmark.Make("Title", "https://example.com", null, null, Now);
        Assert.Throws<Refusal>(() => link.FavoriteAt(double.NaN, Now));
    }

    [Fact]
    public void Moving_a_folder_cannot_cycle_or_push_its_descendants_beyond_the_limit()
    {
        var root = BookmarkFolder.Make("Root", null, false, Now);
        var child = BookmarkFolder.Make("Child", root.Id, false, Now);
        Assert.Throws<Refusal>(() => BookmarkFolder.CheckPlacement(root.Id, child.Id, [root, child]));
        Assert.Throws<Refusal>(() => BookmarkFolder.CheckPlacement(null, Guid.NewGuid(), [root]));
        var tree = new List<BookmarkFolder> { root };
        for (var depth = 2; depth <= BookmarkFolder.MaxDepth; depth++)
            tree.Add(BookmarkFolder.Make("Level", tree[^1].Id, false, Now));
        BookmarkFolder.CheckPlacement(tree[^1].Id, tree[^2].Id, tree);
        Assert.Throws<Refusal>(() => BookmarkFolder.CheckPlacement(null, tree[^1].Id, tree));
        var other = BookmarkFolder.Make("Other", null, false, Now);
        tree.Add(other);
        Assert.Throws<Refusal>(() => BookmarkFolder.CheckPlacement(root.Id, other.Id, tree));
    }

    [Fact]
    public void Existing_permission_shapes_grant_no_bookmark_access()
    {
        var previous = new Permissions(Permission.ReadWrite, Permission.ReadWrite, Permission.ReadWrite, Permission.ReadWrite);
        Assert.False(previous.MayRead(WorkspaceApplication.Bookmarks));
        Assert.True(Permissions.Full.MayWrite(WorkspaceApplication.Bookmarks));
    }
}
