using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Personalaffe.Application.Acts.Knowledge;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Knowledge end to end, against a real Postgres: the nine addresses, the
/// permission matrix, the guard on every write, the history behind a page, and
/// the export.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class KnowledgeTests(PostgresFixture postgres)
{
    /// <summary>
    /// A body with everything in it a round trip breaks first: a fenced block,
    /// a table whose columns are spaces, trailing whitespace, and characters
    /// outside ASCII.
    /// </summary>
    private const string Body = "# Die Architektur\n\nZeile mit Leerzeichen am Ende   \n\n"
        + "| eins | zwei |\n| --- | --- |\n| ja | 日本語 🙂 |\n\n```sh\npea knowledge tree\n```\n";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Markdown_survives_a_round_trip_byte_for_byte()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var written = await wiki.WriteAsync("Die Architektur", null, Body, Token);

        Assert.Equal(HttpStatusCode.Created, written.Status);
        Assert.NotNull(written.ETag);

        var read = await wiki.ReadAsync(written.Id, Token);

        Assert.Equal(Body, read.Body!["markdown"]!.GetValue<string>());
        Assert.Equal(
            Encoding.UTF8.GetByteCount(Body),
            Encoding.UTF8.GetByteCount(read.Body["markdown"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_page_answers_at_the_same_address_through_everything_done_to_it()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var parent = await wiki.WriteAsync("Notizen", null, string.Empty, Token);
        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);

        var address = page.Id;

        var moved = await wiki.RewriteAsync(
            address, "Architecture", parent.Id, "something else", page.Version, Token);
        Assert.Equal(HttpStatusCode.OK, moved.Status);

        var history = await wiki.HistoryAsync(address, Token);
        var revision = history.Body!["items"]!.AsArray().Single()!["id"]!.GetValue<Guid>();

        var recovered = await wiki.RecoverAsync(address, revision, moved.Version, Token);
        Assert.Equal(HttpStatusCode.OK, recovered.Status);

        // Renamed, moved, rewritten and recovered, and the address a link was
        // written against a year ago still answers.
        var read = await wiki.ReadAsync(address, Token);

        Assert.Equal(HttpStatusCode.OK, read.Status);
        Assert.Equal("Die Architektur", read.Body!["title"]!.GetValue<string>());
        Assert.Equal(Body, read.Body["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_tree_is_flat_and_carries_no_bodies()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var parent = await wiki.WriteAsync("Notizen", null, Body, Token);
        await wiki.WriteAsync("Die Architektur", parent.Id, Body, Token);

        var tree = await wiki.TreeAsync(Token);
        var pages = tree.Body!["pages"]!.AsArray();

        Assert.Equal(2, pages.Count);
        Assert.Contains(pages, page => page!["parent"]?.GetValue<Guid>() == parent.Id);

        // A listing that carried every page's Markdown would get slower the more
        // the owner writes, and navigating is what they do most.
        foreach (var page in pages)
        {
            Assert.Null(page!["markdown"]);
        }
    }

    [Fact]
    public async Task Every_change_to_a_page_leaves_what_it_replaced_behind()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, "erste Fassung", Token);

        // Nothing yet: a history whose first entry is "it was empty" would be a
        // history with a lie at the bottom.
        Assert.Empty((await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray());

        var second = await wiki.RewriteAsync(page.Id, "Die Architektur", null, "zweite Fassung", page.Version, Token);
        var third = await wiki.RewriteAsync(page.Id, "Architecture", null, "dritte Fassung", second.Version, Token);

        var items = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray();

        Assert.Equal(2, items.Count);

        // Newest first, and each carries the title it had — a rename is a change
        // to the page like any other.
        Assert.Equal("Die Architektur", items[0]!["title"]!.GetValue<string>());
        Assert.Equal("owner", items[0]!["by"]!["kind"]!.GetValue<string>());

        var version = await wiki.OldVersionAsync(page.Id, items[0]!["id"]!.GetValue<Guid>(), Token);

        Assert.Equal("zweite Fassung", version.Body!["markdown"]!.GetValue<string>());

        // And a history carries no bodies, because fifty of a mebibyte each is
        // not a read anybody should have to make to see when something changed.
        foreach (var item in items)
        {
            Assert.Null(item!["markdown"]);
        }

        Assert.Equal(HttpStatusCode.OK, third.Status);
    }

    [Fact]
    public async Task A_write_that_changes_nothing_leaves_no_revision()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);

        var again = await wiki.RewriteAsync(page.Id, "Die Architektur", null, Body, page.Version, Token);

        // Still guarded and still checked — a write that agreed with what is
        // stored is still a write somebody made from a stale screen — but a
        // history of moments when nothing happened is a history nobody can read.
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Empty((await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray());
    }

    [Fact]
    public async Task Recovering_writes_forward_and_never_loses_what_it_replaced()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, "erste Fassung", Token);
        var second = await wiki.RewriteAsync(page.Id, "Die Architektur", null, "zweite Fassung", page.Version, Token);

        var first = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray()
            .Single()!["id"]!.GetValue<Guid>();

        var recovered = await wiki.RecoverAsync(page.Id, first, second.Version, Token);

        Assert.Equal(HttpStatusCode.OK, recovered.Status);
        Assert.Equal("erste Fassung", recovered.Body!["markdown"]!.GetValue<string>());

        // "Undo" that destroyed work would be the one operation in this product
        // that does. The version it replaced is a revision of its own now.
        var items = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray();

        Assert.Equal(2, items.Count);

        var newest = await wiki.OldVersionAsync(page.Id, items[0]!["id"]!.GetValue<Guid>(), Token);

        Assert.Equal("zweite Fassung", newest.Body!["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_page_keeps_fifty_versions_and_drops_the_one_that_falls_off()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, "Fassung 0", Token);
        var version = page.Version;

        for (var edit = 1; edit <= Personalaffe.Domain.Revisions.Kept + 2; edit++)
        {
            var written = await wiki.RewriteAsync(
                page.Id, "Die Architektur", null, $"Fassung {edit}", version, Token);

            Assert.Equal(HttpStatusCode.OK, written.Status);
            version = written.Version;
        }

        var items = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray();

        Assert.Equal(Personalaffe.Domain.Revisions.Kept, items.Count);

        // The rows went with them: a history that only looked bounded would be
        // a table that grows forever.
        var (_, revisions) = await wiki.RowsAsync(Token);
        Assert.Equal(Personalaffe.Domain.Revisions.Kept, revisions);
    }

    [Fact]
    public async Task Two_writes_from_one_read_and_the_second_changes_nothing()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, "erste Fassung", Token);

        Assert.Equal(
            HttpStatusCode.OK,
            (await wiki.RewriteAsync(page.Id, "Die Architektur", null, "meine Fassung", page.Version, Token)).Status);

        var stale = await wiki.RewriteAsync(
            page.Id, "Die Architektur", null, "deine Fassung", page.Version, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal("stale", stale.Code);

        Assert.Equal(
            "meine Fassung",
            (await wiki.ReadAsync(page.Id, Token)).Body!["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task Recovering_from_a_stale_read_is_refused_too()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, "erste Fassung", Token);
        var second = await wiki.RewriteAsync(page.Id, "Die Architektur", null, "zweite Fassung", page.Version, Token);

        var revision = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray()
            .Single()!["id"]!.GetValue<Guid>();

        await wiki.RewriteAsync(page.Id, "Die Architektur", null, "dritte Fassung", second.Version, Token);

        // Recovering something read ten minutes ago would otherwise discard an
        // edit made five minutes ago.
        var stale = await wiki.RecoverAsync(page.Id, revision, second.Version, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal(
            "dritte Fassung",
            (await wiki.ReadAsync(page.Id, Token)).Body!["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_title_already_taken_where_it_is_going_is_a_conflict()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        await wiki.WriteAsync("Die Architektur", null, Body, Token);

        // Capitals do not make it a different title.
        var refused = await wiki.WriteAsync("die architektur", null, Body, Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("conflict", refused.Code);

        // The same title under another page is another page.
        var parent = await wiki.WriteAsync("Notizen", null, string.Empty, Token);

        Assert.Equal(
            HttpStatusCode.Created,
            (await wiki.WriteAsync("Die Architektur", parent.Id, Body, Token)).Status);
    }

    [Fact]
    public async Task A_page_cannot_be_put_under_itself_or_under_its_own_child()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var top = await wiki.WriteAsync("Notizen", null, string.Empty, Token);
        var under = await wiki.WriteAsync("Die Architektur", top.Id, Body, Token);

        var itself = await wiki.RewriteAsync(top.Id, "Notizen", top.Id, string.Empty, top.Version, Token);
        Assert.Equal(HttpStatusCode.Conflict, itself.Status);

        var beneath = await wiki.RewriteAsync(top.Id, "Notizen", under.Id, string.Empty, top.Version, Token);

        Assert.Equal(HttpStatusCode.Conflict, beneath.Status);
        Assert.Contains("already under it", beneath.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_tree_has_a_bottom_and_says_so()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        Guid? parent = null;

        for (var depth = 0; depth < Page.MaxDepth; depth++)
        {
            var written = await wiki.WriteAsync($"Ebene {depth}", parent, string.Empty, Token);

            Assert.Equal(HttpStatusCode.Created, written.Status);
            parent = written.Id;
        }

        var refused = await wiki.WriteAsync("eine zu tief", parent, string.Empty, Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Contains(
            Page.MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refused.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deleted_page_takes_what_is_under_it_and_all_of_its_history()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var top = await wiki.WriteAsync("Notizen", null, "erste Fassung", Token);
        var under = await wiki.WriteAsync("Die Architektur", top.Id, Body, Token);

        var edited = await wiki.RewriteAsync(top.Id, "Notizen", null, "zweite Fassung", top.Version, Token);

        Assert.Equal(HttpStatusCode.NoContent, (await wiki.DiscardAsync(top.Id, edited.Version, Token)).Status);

        Assert.Empty((await wiki.TreeAsync(Token)).Body!["pages"]!.AsArray());
        Assert.Equal("deleted", (await wiki.ReadAsync(under.Id, Token)).Code);

        // One Trash entry: the page the owner deleted, not two things to put
        // back one at a time.
        var items = (await wiki.TrashAsync(Token))["items"]!.AsArray();

        Assert.Single(items);
        Assert.Equal("Notizen", items[0]!["name"]!.GetValue<string>());

        var restored = await wiki.RestoreAsync(top.Id, Versions.Of(items[0]!), Token);
        Assert.Equal(HttpStatusCode.OK, restored.Status);

        // And the history came back with it: a revision that outlived its page
        // would be content the owner believes they deleted.
        Assert.Single((await wiki.HistoryAsync(top.Id, Token)).Body!["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, (await wiki.ReadAsync(under.Id, Token)).Status);
    }

    [Fact]
    public async Task Removing_a_page_for_good_removes_its_history_with_it()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, "erste Fassung", Token);
        var edited = await wiki.RewriteAsync(page.Id, "Die Architektur", null, "zweite Fassung", page.Version, Token);

        await wiki.DiscardAsync(page.Id, edited.Version, Token);
        await wiki.BackdateAsync(TimeSpan.FromDays(31), Token);

        var swept = await wiki.PurgeAsync(Token);

        Assert.True(swept.Swept);
        Assert.Equal(1, swept.Total);

        var (pages, revisions) = await wiki.RowsAsync(Token);

        Assert.Equal(0, pages);
        Assert.Equal(0, revisions);
    }

    [Fact]
    public async Task An_agent_that_may_read_knowledge_may_not_change_it()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);
        var revision = Guid.CreateVersion7(DateTimeOffset.UtcNow);

        using var reader = await wiki.AnAgentReachingAsync("read", Token);

        Assert.Equal(HttpStatusCode.OK, (await wiki.TreeAsync(Token, reader)).Status);
        Assert.Equal(HttpStatusCode.OK, (await wiki.ReadAsync(page.Id, Token, reader)).Status);
        Assert.Equal(HttpStatusCode.OK, (await wiki.HistoryAsync(page.Id, Token, reader)).Status);

        var (exported, _, _) = await wiki.ExportAsync(Token, reader);
        Assert.Equal(HttpStatusCode.OK, exported);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await wiki.WriteAsync("Noch eine", null, Body, Token, reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await wiki.RewriteAsync(page.Id, "x", null, Body, page.Version, Token, reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await wiki.DiscardAsync(page.Id, page.Version, Token, reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await wiki.RecoverAsync(page.Id, revision, page.Version, Token, reader)).Status);
    }

    [Fact]
    public async Task An_agent_without_knowledge_cannot_reach_a_word_of_it()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);
        using var stranger = await wiki.AnAgentReachingAsync("none", Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await wiki.TreeAsync(Token, stranger)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await wiki.ReadAsync(page.Id, Token, stranger)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await wiki.HistoryAsync(page.Id, Token, stranger)).Status);

        // The export most of all: it is every page at once.
        var (status, zip, _) = await wiki.ExportAsync(Token, stranger);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.DoesNotContain("Architektur", Encoding.UTF8.GetString(zip), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_switched_off_knowledge_refuses_every_address_and_keeps_everything()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);

        await wiki.SwitchAsync(enabled: false, Token);

        Assert.Equal("disabled", (await wiki.TreeAsync(Token)).Code);
        Assert.Equal("disabled", (await wiki.ReadAsync(page.Id, Token)).Code);
        Assert.Equal("disabled", (await wiki.WriteAsync("Noch eine", null, Body, Token)).Code);

        var (status, _, _) = await wiki.ExportAsync(Token);
        Assert.Equal(HttpStatusCode.Conflict, status);

        await wiki.SwitchAsync(enabled: true, Token);

        Assert.Equal(Body, (await wiki.ReadAsync(page.Id, Token)).Body!["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_export_is_a_zip_of_markdown_anybody_can_read()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var top = await wiki.WriteAsync("Reisen", null, "# Reisen\n", Token);
        var under = await wiki.WriteAsync("Bahn: 2026?", top.Id, Body, Token);

        var (status, zip, disposition) = await wiki.ExportAsync(Token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("attachment", disposition ?? string.Empty, StringComparison.Ordinal);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);

        var names = archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();

        // The hierarchy is the directory layout, which is the one form a person
        // can see without being told anything. The `:` and the `?` are replaced
        // because Windows refuses them in a name; the real title is in the front
        // matter.
        Assert.Contains(ExportTheKnowledge.Index, names);
        Assert.Contains($"{ExportTheKnowledge.Directory}/Reisen.md", names);
        Assert.Contains($"{ExportTheKnowledge.Directory}/Reisen/Bahn- 2026-.md", names);

        var written = await ReadAsync(archive, $"{ExportTheKnowledge.Directory}/Reisen/Bahn- 2026-.md");

        Assert.StartsWith("---\n", written, StringComparison.Ordinal);
        Assert.Contains($"id: {under.Id}", written, StringComparison.Ordinal);
        Assert.Contains($"parent: {top.Id}", written, StringComparison.Ordinal);
        Assert.Contains("title: \"Bahn: 2026?\"", written, StringComparison.Ordinal);

        // And the body is the body: an export that reformatted anything would be
        // an export of something slightly different from what the owner has.
        Assert.EndsWith(Body, written, StringComparison.Ordinal);

        var index = JsonNode.Parse(await ReadAsync(archive, ExportTheKnowledge.Index))!;

        Assert.Equal(2, index["pages"]!.AsArray().Count);
        Assert.Contains(
            index["pages"]!.AsArray(),
            page => page!["id"]!.GetValue<Guid>() == under.Id
                    && page["file"]!.GetValue<string>().EndsWith("Bahn- 2026-.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_field_a_request_does_not_define_is_said_out_loud()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        using var refused = await wiki.Owner.PostAsJsonAsync(
            "/api/knowledge/pages", new { title = "Die Architektur", parrent = (Guid?)null }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var body = JsonNode.Parse(await refused.Content.ReadAsStringAsync(Token))!;
        Assert.Equal("/problems/unknown-field", body["type"]!.GetValue<string>());
    }

    private static async Task<string> ReadAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);

        Assert.NotNull(entry);

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);

        return await reader.ReadToEndAsync(Token);
    }
}
