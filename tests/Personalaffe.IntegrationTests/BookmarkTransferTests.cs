using System.Net;
using System.Text.Json.Nodes;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarkTransferTests(PostgresFixture postgres)
{
    private const string Html = "<DL><DT><H3>旅行 &amp; Research</H3><DL><DT><A HREF='HTTPS://EXAMPLE.COM:443/a?x=1&amp;y=2#frag'>Guide &lt;one&gt;</A></DT><DD>A &amp; B</DD><DT><A HREF='https://example.com/a?x=1&amp;y=2#frag'>Duplicate</A></DT><DT><A HREF='javascript:bad()'>Bad</A></DT></DL></DT></DL>";

    [Fact]
    public async Task Preview_is_inert_import_is_confirmed_atomic_and_repeatable_with_supported_html_roundtrip()
    {
        await using var books = await ABookmarkCollection.StartedAsync(postgres);
        var preview = await books.Send(HttpMethod.Post, "/api/bookmarks/import/preview", new { html = Html });
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Equal(1, preview.Body!["new_bookmarks"]!.GetValue<int>());
        Assert.Equal(1, preview.Body["skipped_duplicates"]!.GetValue<int>());
        Assert.Single(preview.Body["rejected"]!.AsArray());
        Assert.Empty((await books.Send(HttpMethod.Get, "/api/bookmarks")).Body!["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.Conflict, (await books.Send(HttpMethod.Post, "/api/bookmarks/import", new { html = Html })).Status);
        var imported = await books.Send(HttpMethod.Post, "/api/bookmarks/import", new { html = Html, preview_hash = preview.Body["preview_hash"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.OK, imported.Status);
        Assert.Equal(1, imported.Body!["imported_bookmarks"]!.GetValue<int>());
        var again = await books.Send(HttpMethod.Post, "/api/bookmarks/import/preview", new { html = Html });
        Assert.Equal(0, again.Body!["new_bookmarks"]!.GetValue<int>());
        Assert.Equal(0, again.Body["new_folders"]!.GetValue<int>());
        await books.Send(HttpMethod.Post, "/api/bookmarks/import", new { html = Html, preview_hash = again.Body["preview_hash"]!.GetValue<string>() });
        var exported = await books.Send(HttpMethod.Get, "/api/bookmarks/export");
        var html = exported.Body!["html"]!.GetValue<string>();
        Assert.Contains("Guide &lt;one&gt;", html); Assert.Contains("A &amp; B", html); Assert.DoesNotContain("javascript:", html);
        var destination = await books.Folder("Roundtrip");
        var roundtrip = await books.Send(HttpMethod.Post, "/api/bookmarks/import/preview", new { html, folder = destination.Id });
        Assert.Equal(1, roundtrip.Body!["new_bookmarks"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.OK, (await books.Send(HttpMethod.Post, "/api/bookmarks/import", new { html, folder = destination.Id, preview_hash = roundtrip.Body["preview_hash"]!.GetValue<string>() })).Status);
        var rows = (await books.Send(HttpMethod.Get, "/api/bookmarks")).Body!["items"]!.AsArray();
        Assert.Equal(2, rows.Count); Assert.All(rows, row => Assert.Equal("A & B", row!["description"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Private_transfer_requires_context_and_export_opt_in_and_rechecks_preview_state()
    {
        await using var books = await ABookmarkCollection.StartedAsync(postgres);
        var folder = await books.Folder("Secret folder", isPrivate: true, context: true);
        var secret = await books.Link("Secret", folder.Id, context: true);
        var hidden = await books.Send(HttpMethod.Post, "/api/bookmarks/import/preview", new { html = Html, folder = folder.Id });
        Assert.Equal(HttpStatusCode.NotFound, hidden.Status);
        var preview = await books.Send(HttpMethod.Post, "/api/bookmarks/import/preview", new { html = Html, folder = folder.Id }, context: true);
        Assert.Equal(HttpStatusCode.NotFound, (await books.Send(HttpMethod.Post, "/api/bookmarks/import", new { html = Html, folder = folder.Id, preview_hash = preview.Body!["preview_hash"]!.GetValue<string>() })).Status);
        Assert.Equal(0, (await books.Send(HttpMethod.Get, "/api/bookmarks/export", context: true)).Body!["bookmarks"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.BadRequest, (await books.Send(HttpMethod.Get, "/api/bookmarks/export?include_private=true")).Status);
        Assert.Contains("Secret", (await books.Send(HttpMethod.Get, "/api/bookmarks/export?include_private=true", context: true)).Body!["html"]!.GetValue<string>());
        await books.Link("Another");
        Assert.Equal(HttpStatusCode.Conflict, (await books.Send(HttpMethod.Post, "/api/bookmarks/import", new { html = Html, folder = folder.Id, preview_hash = preview.Body!["preview_hash"]!.GetValue<string>() }, context: true)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await books.Send(HttpMethod.Get, $"/api/bookmarks/{secret.Id}")).Status);
    }

    [Fact]
    public async Task Depth_failure_writes_nothing_and_invisible_duplicates_do_not_change_preview()
    {
        await using var books = await ABookmarkCollection.StartedAsync(postgres);
        var first = await books.Send(HttpMethod.Post, "/api/bookmarks/import/preview", new { html = Html });
        var hidden = await books.Folder("Hidden", isPrivate: true, context: true);
        await books.Link("Invisible", hidden.Id, context: true);
        var second = await books.Send(HttpMethod.Post, "/api/bookmarks/import/preview", new { html = Html });
        Assert.Equal(first.Body!.ToJsonString(), second.Body!.ToJsonString());
        var tooDeep = string.Concat(Enumerable.Repeat("<DL><DT><H3>Deep</H3>", 34)) + "<DL><A HREF='https://example.com'>Link</A>";
        Assert.Equal(HttpStatusCode.BadRequest, (await books.Send(HttpMethod.Post, "/api/bookmarks/import/preview", new { html = tooDeep })).Status);
        Assert.Empty((await books.Send(HttpMethod.Get, "/api/bookmarks/folders")).Body!["items"]!.AsArray());
        using var reader = await books.Agent("read");
        Assert.Equal(HttpStatusCode.Forbidden, (await books.Send(HttpMethod.Post, "/api/bookmarks/import", new { html = Html, preview_hash = first.Body["preview_hash"]!.GetValue<string>() }, client: reader)).Status);
    }
}
