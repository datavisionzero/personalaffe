using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Personalaffe.Application.Acts.Knowledge;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// PERSONAL-E7's promises, one test each, end to end against a real Postgres —
/// the shape <see cref="TheDoorHoldsTests"/>, <see cref="TheSafeguardsHoldTests"/>
/// and <see cref="TheFilesHoldTests"/> gave the epics before it.
/// </summary>
/// <remarks>
/// The surfaces are covered in detail elsewhere: <see cref="KnowledgeTests"/> is
/// the API, <c>src/cli/internal/cmd/knowledge_test.go</c> the CLI and
/// <c>src/web/browser/knowledge.spec.ts</c> the browser. What is here is the
/// epic's own list, so that a promise nobody can find a test for is a promise
/// that is not kept.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheKnowledgeHoldsTests(PostgresFixture postgres)
{
    /// <summary>
    /// A body with everything in it a round trip breaks first: a fenced block,
    /// a table whose columns are spaces, trailing whitespace, a blank line at
    /// the end, and characters outside ASCII.
    /// </summary>
    private const string Body = "# Die Architektur\n\nZeile mit Leerzeichen am Ende   \n\n"
        + "| eins | zwei |\n| --- | --- |\n| ja | 日本語 🙂 |\n\n```sh\npea knowledge tree\n```\n\n";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_human_and_an_agent_do_the_same_things_to_the_same_pages()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        using var agent = await wiki.AnAgentReachingAsync("read_write", Token);

        var written = await wiki.WriteAsync("Die Architektur", null, Body, Token, agent);
        Assert.Equal(HttpStatusCode.Created, written.Status);

        var read = await wiki.ReadAsync(written.Id, Token);
        Assert.Equal(Body, read.Body!["markdown"]!.GetValue<string>());

        var moved = await wiki.RewriteAsync(
            written.Id, "Architecture", null, Body, read.Version, Token);
        Assert.Equal(HttpStatusCode.OK, moved.Status);

        // What the agent wrote, the owner recovers; what the owner changed, the
        // agent can read the history of. Neither is a second class of caller.
        var revision = (await wiki.HistoryAsync(written.Id, Token, agent))
            .Body!["items"]!.AsArray().Single()!["id"]!.GetValue<Guid>();

        var recovered = await wiki.RecoverAsync(written.Id, revision, moved.Version, Token, agent);

        Assert.Equal(HttpStatusCode.OK, recovered.Status);
        Assert.Equal("Die Architektur", recovered.Body!["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_page_address_still_resolves_after_everything_that_can_be_done_to_a_page()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var under = await wiki.WriteAsync("Notizen", null, string.Empty, Token);
        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);

        var address = $"/api/knowledge/pages/{page.Id}";

        var moved = await wiki.RewriteAsync(page.Id, "Architecture", under.Id, "anders", page.Version, Token);
        var revision = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray()
            .Single()!["id"]!.GetValue<Guid>();

        await wiki.RecoverAsync(page.Id, revision, moved.Version, Token);

        using var response = await wiki.Owner.GetAsync(address, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;

        Assert.Equal("Die Architektur", body["title"]!.GetValue<string>());
        Assert.Equal(Body, body["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task Nothing_a_caller_can_ask_for_corrupts_the_tree()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var top = await wiki.WriteAsync("Notizen", null, string.Empty, Token);
        var under = await wiki.WriteAsync("Die Architektur", top.Id, Body, Token);

        // Inside itself, inside its own child, under a page that is not there,
        // and under a title a sibling already has.
        foreach (var attempt in (Guid?[])[top.Id, under.Id])
        {
            Assert.Equal(
                HttpStatusCode.Conflict,
                (await wiki.RewriteAsync(top.Id, "Notizen", attempt, string.Empty, top.Version, Token)).Status);
        }

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await wiki.WriteAsync("Irgendwo", Guid.CreateVersion7(DateTimeOffset.UtcNow), string.Empty, Token))
                .Status);

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await wiki.WriteAsync("notizen", null, string.Empty, Token)).Status);

        // And the tree is exactly what it was.
        var pages = (await wiki.TreeAsync(Token)).Body!["pages"]!.AsArray();

        Assert.Equal(2, pages.Count);
        Assert.Equal(
            top.Id,
            pages.First(page => page!["id"]!.GetValue<Guid>() == under.Id)!["parent"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Markdown_survives_a_round_trip_and_so_does_an_empty_page()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);
        var empty = await wiki.WriteAsync("Noch nichts", null, null, Token);

        Assert.Equal(
            Encoding.UTF8.GetByteCount(Body),
            Encoding.UTF8.GetByteCount(
                (await wiki.ReadAsync(page.Id, Token)).Body!["markdown"]!.GetValue<string>()));

        // Nothing is trimmed: a blank line at the end of a page is the
        // person's, unlike a Scratchpad entry's trailing newline.
        Assert.Equal(Body, (await wiki.ReadAsync(page.Id, Token)).Body!["markdown"]!.GetValue<string>());

        // A title with nothing under it yet is how a knowledge base usually
        // starts a page.
        Assert.Equal(
            string.Empty, (await wiki.ReadAsync(empty.Id, Token)).Body!["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task History_only_ever_grows_and_recovering_is_what_proves_it()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, "eins", Token);
        var second = await wiki.RewriteAsync(page.Id, "Die Architektur", null, "zwei", page.Version, Token);
        var third = await wiki.RewriteAsync(page.Id, "Die Architektur", null, "drei", second.Version, Token);

        var oldest = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray()
            .Last()!["id"]!.GetValue<Guid>();

        var recovered = await wiki.RecoverAsync(page.Id, oldest, third.Version, Token);

        Assert.Equal("eins", recovered.Body!["markdown"]!.GetValue<string>());

        // Three versions behind it now, not one: undoing has never destroyed
        // anything, and "drei" is still there to go back to.
        var items = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray();

        Assert.Equal(3, items.Count);

        var newest = await wiki.OldVersionAsync(page.Id, items[0]!["id"]!.GetValue<Guid>(), Token);

        Assert.Equal("drei", newest.Body!["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_outdated_write_and_an_outdated_recovery_both_change_nothing()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, "eins", Token);
        var second = await wiki.RewriteAsync(page.Id, "Die Architektur", null, "zwei", page.Version, Token);

        var revision = (await wiki.HistoryAsync(page.Id, Token)).Body!["items"]!.AsArray()
            .Single()!["id"]!.GetValue<Guid>();

        var third = await wiki.RewriteAsync(page.Id, "Die Architektur", null, "drei", second.Version, Token);

        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            (await wiki.RewriteAsync(page.Id, "Die Architektur", null, "aus dem Nichts", second.Version, Token))
                .Status);

        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            (await wiki.RecoverAsync(page.Id, revision, second.Version, Token)).Status);

        Assert.Equal("drei", (await wiki.ReadAsync(page.Id, Token)).Body!["markdown"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, third.Status);
    }

    [Fact]
    public async Task A_deleted_page_is_absent_from_every_ordinary_read_and_comes_back_whole()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var top = await wiki.WriteAsync("Notizen", null, "eins", Token);
        var under = await wiki.WriteAsync("Die Architektur", top.Id, Body, Token);
        var edited = await wiki.RewriteAsync(top.Id, "Notizen", null, "zwei", top.Version, Token);

        await wiki.DiscardAsync(top.Id, edited.Version, Token);

        Assert.Empty((await wiki.TreeAsync(Token)).Body!["pages"]!.AsArray());
        Assert.Equal("deleted", (await wiki.ReadAsync(under.Id, Token)).Code);
        Assert.Equal("deleted", (await wiki.HistoryAsync(top.Id, Token)).Code);

        var entry = (await wiki.TrashAsync(Token))["items"]!.AsArray().Single()!;

        Assert.Equal(HttpStatusCode.OK, (await wiki.RestoreAsync(top.Id, Versions.Of(entry), Token)).Status);

        Assert.Equal(2, (await wiki.TreeAsync(Token)).Body!["pages"]!.AsArray().Count);
        Assert.Single((await wiki.HistoryAsync(top.Id, Token)).Body!["items"]!.AsArray());
        Assert.Equal(Body, (await wiki.ReadAsync(under.Id, Token)).Body!["markdown"]!.GetValue<string>());
    }

    [Fact]
    public async Task What_the_owner_switched_off_is_absent_from_every_surface_and_kept()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);
        var deleted = await wiki.WriteAsync("Weg damit", null, "eins", Token);

        await wiki.DiscardAsync(deleted.Id, deleted.Version, Token);
        await wiki.SwitchAsync(enabled: false, Token);

        Assert.Equal("disabled", (await wiki.TreeAsync(Token)).Code);
        Assert.Equal("disabled", (await wiki.ReadAsync(page.Id, Token)).Code);

        // An aggregate view leaves it out rather than refusing.
        Assert.Empty((await wiki.TrashAsync(Token))["items"]!.AsArray());

        await wiki.SwitchAsync(enabled: true, Token);

        Assert.Equal(Body, (await wiki.ReadAsync(page.Id, Token)).Body!["markdown"]!.GetValue<string>());
        Assert.Single((await wiki.TrashAsync(Token))["items"]!.AsArray());
    }

    [Fact]
    public async Task A_page_linking_to_a_file_can_reach_no_more_of_it_than_the_reader_can()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var file = await wiki.UploadAsync("plan.txt", "die Bytes"u8.ToArray(), Token);
        var page = await wiki.WriteAsync(
            "Die Architektur", null, $"Siehe [den Plan](file:{file}).", Token);

        var address = $"/api/files/{file}/content";

        // The owner: both.
        Assert.Equal(HttpStatusCode.OK, (await wiki.Owner.GetAsync(address, Token)).StatusCode);

        // An agent the owner gave Knowledge and not Files reads the page — the
        // link is text, and text is all a page ever holds — and gets nothing at
        // the other end of it.
        using var reader = await wiki.AnAgentReachingAsync("read", Token);

        Assert.Equal(HttpStatusCode.OK, (await wiki.ReadAsync(page.Id, Token, reader)).Status);

        using var denied = await reader.GetAsync(address, Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // And Files switched off answers the same way to the owner: a link into
        // an application that is off leads nowhere, and the page it is written
        // on is untouched.
        await wiki.SwitchAsync("files", enabled: false, Token);

        using var off = await wiki.Owner.GetAsync(address, Token);

        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await wiki.ReadAsync(page.Id, Token)).Status);
    }

    [Fact]
    public async Task The_export_is_readable_by_somebody_who_never_heard_of_this_product()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var top = await wiki.WriteAsync("Reisen", null, "# Reisen\n", Token);
        var under = await wiki.WriteAsync("Bahn", top.Id, Body, Token);

        var (status, zip, _) = await wiki.ExportAsync(Token);

        Assert.Equal(HttpStatusCode.OK, status);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);

        // Unzip it and read it: the directory layout is the hierarchy, and the
        // file is the Markdown with a header a person can read.
        var written = await ReadAsync(archive, $"{ExportTheKnowledge.Directory}/Reisen/Bahn.md");

        Assert.Contains($"id: {under.Id}", written, StringComparison.Ordinal);
        Assert.Contains($"parent: {top.Id}", written, StringComparison.Ordinal);
        Assert.EndsWith(Body, written, StringComparison.Ordinal);

        // And what a directory layout cannot carry is in the index as well.
        var index = JsonNode.Parse(await ReadAsync(archive, ExportTheKnowledge.Index))!;

        Assert.Equal(2, index["pages"]!.AsArray().Count);

        var top_ = index["pages"]!.AsArray().First(one => one!["id"]!.GetValue<Guid>() == top.Id)!;

        Assert.Null(top_["parent"]?.GetValue<Guid?>());
        Assert.Equal("Reisen", top_["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Every_address_of_this_application_is_behind_the_door()
    {
        await using var wiki = await AKnowledgeBase.StartedAsync(postgres, Token);

        var page = await wiki.WriteAsync("Die Architektur", null, Body, Token);
        var revision = Guid.CreateVersion7(DateTimeOffset.UtcNow);

        using var stranger = wiki.Instance.CreateClient();

        foreach (var address in (string[])
                 [
                     "/api/knowledge/pages",
                     $"/api/knowledge/pages/{page.Id}",
                     $"/api/knowledge/pages/{page.Id}/revisions",
                     $"/api/knowledge/pages/{page.Id}/revisions/{revision}",
                     "/api/knowledge/export",
                 ])
        {
            using var response = await stranger.GetAsync(address, Token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // And an agent the owner gave nothing gets no word of it, the export
        // included — it is every page at once.
        using var none = await wiki.AnAgentReachingAsync("none", Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await wiki.TreeAsync(Token, none)).Status);

        var (status, zip, _) = await wiki.ExportAsync(Token, none);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.DoesNotContain("Architektur", Encoding.UTF8.GetString(zip), StringComparison.Ordinal);
    }

    private static async Task<string> ReadAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);

        Assert.NotNull(entry);

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);

        return await reader.ReadToEndAsync(Token);
    }
}
