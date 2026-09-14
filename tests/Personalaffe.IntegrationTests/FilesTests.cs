using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Files;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The Files API end to end, against a real Postgres and a real storage root:
/// the ten addresses, the permission matrix, the guard on every write, the two
/// limits, and the reference a rename cannot break.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FilesTests(PostgresFixture postgres)
{
    /// <summary>
    /// A file with everything in it that breaks first: bytes that are not text,
    /// a NUL in the middle, and every value a byte can have.
    /// </summary>
    private static readonly byte[] Bytes =
        [.. Enumerable.Range(0, 256).Select(value => (byte)value), 0, 0, 0xFF];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_file_stored_on_one_device_comes_back_byte_for_byte_on_another()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("Größe 🙂.bin", Bytes, null, Token, "image/png");

        Assert.Equal(HttpStatusCode.Created, stored.Status);
        Assert.Equal("Größe 🙂.bin", stored.Body!["name"]!.GetValue<string>());
        Assert.Equal(Bytes.Length, stored.Body["size"]!.GetValue<long>());
        Assert.Equal("image/png", stored.Body["media_type"]!.GetValue<string>());
        Assert.NotNull(stored.ETag);

        // The other device: a second browser of the same owner's.
        using var phone = await AnOwner.SignInAgainAsync(area.Instance, Token);

        var (status, content, headers, body) = await area.DownloadAsync(stored.Id, Token, phone);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Bytes, content);
        Assert.Equal("image/png", body.ContentType?.MediaType);

        // Whatever a file says it is, the browser is told not to work it out
        // for itself and to save it rather than show it.
        Assert.Equal("nosniff", headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", body.ContentDisposition?.DispositionType);
        Assert.Equal("Größe 🙂.bin", body.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task The_address_of_a_file_survives_every_rename_and_every_move()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("draft.pdf", Bytes, null, Token);
        var folder = await area.MakeFolderAsync("Reisen", null, Token);
        var deeper = await area.MakeFolderAsync("2026", folder.Id, Token);

        var moved = await area.ChangeFileAsync(
            stored.Id, "Reisekosten 2026.pdf", deeper.Id, stored.Version, Token);

        Assert.Equal(HttpStatusCode.OK, moved.Status);
        Assert.Equal(deeper.Id, moved.Body!["folder"]!.GetValue<Guid>());

        // The whole point of the reference being the id: a link written before
        // the rename still answers with the same bytes (docs/mvp-plan.md,
        // PERSONAL-E6). Knowledge links to exactly this in PERSONAL-E7.
        var (status, content, _, _) = await area.DownloadAsync(stored.Id, Token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Bytes, content);
    }

    [Fact]
    public async Task A_listing_says_where_it_is_and_how_much_room_is_left()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var year = await area.MakeFolderAsync("2026", reisen.Id, Token);

        await area.UploadAsync("bahn.pdf", Bytes, year.Id, Token);
        await area.MakeFolderAsync("Belege", year.Id, Token);

        var listing = await area.ListAsync(year.Id, Token);

        Assert.Equal(HttpStatusCode.OK, listing.Status);

        // The breadcrumb reads the way a person does: from the top down.
        Assert.Equal(
            ["Reisen", "2026"],
            listing.Body!["chain"]!.AsArray().Select(step => step!["name"]!.GetValue<string>()));

        Assert.Equal(["Belege"], listing.Body["folders"]!.AsArray().Select(one => one!["name"]!.GetValue<string>()));
        Assert.Equal(["bahn.pdf"], listing.Body["files"]!.AsArray().Select(one => one!["name"]!.GetValue<string>()));

        Assert.Equal(Bytes.Length, listing.Body["used_bytes"]!.GetValue<long>());
        Assert.Equal(
            StorageSettings.DefaultMaxFileMib * StorageSettings.Mebibyte,
            listing.Body["max_file_bytes"]!.GetValue<long>());
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("notes/plan.md")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("two\nlines")]
    public async Task A_name_that_is_a_path_is_refused_and_nothing_reaches_the_volume(string name)
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var refused = await area.UploadAsync(name, Bytes, null, Token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("validation", refused.Code);

        // Not one byte was read, let alone written: the name is held to the
        // rules before the body is touched.
        Assert.Empty(area.OnTheVolume());
    }

    [Fact]
    public async Task A_name_already_in_the_folder_is_a_conflict_and_never_a_silent_rename()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        await area.UploadAsync("plan.md", Bytes, null, Token);

        // Capitals do not make it a different name: one folder holding `Plan.md`
        // and `plan.md` is a folder nobody can navigate.
        var refused = await area.UploadAsync("Plan.md", Bytes, null, Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("conflict", refused.Code);

        var folder = await area.MakeFolderAsync("PLAN.MD", null, Token);

        Assert.Equal(HttpStatusCode.Conflict, folder.Status);

        // A file and a folder share one namespace, and the refusal took the
        // second upload before it stored anything.
        Assert.Single(area.OnTheVolume());
    }

    [Fact]
    public async Task The_same_name_in_two_folders_is_two_different_things()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var one = await area.MakeFolderAsync("Reisen", null, Token);
        var other = await area.MakeFolderAsync("Belege", null, Token);

        Assert.Equal(HttpStatusCode.Created, (await area.UploadAsync("plan.md", Bytes, one.Id, Token)).Status);
        Assert.Equal(HttpStatusCode.Created, (await area.UploadAsync("plan.md", Bytes, other.Id, Token)).Status);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_and_leaves_nothing_behind()
    {
        await using var area = await AFileArea.StartedAsync(
            postgres,
            Token,
            new Dictionary<string, string?> { [StorageSettings.MaxFileVariable] = "1" });

        var tooMuch = new byte[(int)StorageSettings.Mebibyte + 1];

        var refused = await area.UploadAsync("big.bin", tooMuch, null, Token);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.Status);
        Assert.Equal("too-large", refused.Code);
        Assert.Equal(StorageSettings.Mebibyte, refused.Body!["limit_bytes"]!.GetValue<long>());

        // The half that arrived is gone: a client retrying a failing upload
        // must not fill the volume with copies of it.
        Assert.Empty(area.OnTheVolume());
    }

    [Fact]
    public async Task An_upload_with_no_room_left_is_a_different_refusal_because_the_answer_is_different()
    {
        await using var area = await AFileArea.StartedAsync(
            postgres,
            Token,
            new Dictionary<string, string?>
            {
                [StorageSettings.MaxFileVariable] = "1",
                [StorageSettings.MaxTotalVariable] = "1",
            });

        var most = new byte[StorageSettings.Mebibyte - 16];

        Assert.Equal(HttpStatusCode.Created, (await area.UploadAsync("first.bin", most, null, Token)).Status);

        var refused = await area.UploadAsync("second.bin", new byte[64], null, Token);

        // `too-large` would tell the owner to send something smaller, which is
        // not what would help. The move here is to delete something.
        Assert.Equal(HttpStatusCode.InsufficientStorage, refused.Status);
        Assert.Equal("out-of-space", refused.Code);
        Assert.Contains("Delete something", refused.Detail, StringComparison.Ordinal);

        Assert.Single(area.OnTheVolume());
    }

    [Fact]
    public async Task Replacing_a_file_gives_back_the_room_it_was_using()
    {
        await using var area = await AFileArea.StartedAsync(
            postgres,
            Token,
            new Dictionary<string, string?>
            {
                [StorageSettings.MaxFileVariable] = "1",
                [StorageSettings.MaxTotalVariable] = "1",
            });

        var most = new byte[StorageSettings.Mebibyte - 16];
        var stored = await area.UploadAsync("report.bin", most, null, Token);

        // The same size again. Without counting what this write replaces, a
        // file that already fits could not be rewritten.
        var replaced = await area.ReplaceAsync(stored.Id, most, stored.Version, Token, "application/pdf");

        Assert.Equal(HttpStatusCode.OK, replaced.Status);
        Assert.Equal("application/pdf", replaced.Body!["media_type"]!.GetValue<string>());

        var (_, content, _, _) = await area.DownloadAsync(stored.Id, Token);
        Assert.Equal(most.Length, content.Length);
    }

    [Fact]
    public async Task New_bytes_for_a_file_answer_at_the_address_it_already_had()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", "the first draft"u8.ToArray(), null, Token);
        var replaced = await area.ReplaceAsync(
            stored.Id, "the second draft"u8.ToArray(), stored.Version, Token, "text/markdown");

        Assert.Equal(HttpStatusCode.OK, replaced.Status);

        var (_, content, _, _) = await area.DownloadAsync(stored.Id, Token);

        Assert.Equal("the second draft", Encoding.UTF8.GetString(content));

        // One file, one set of bytes. Nothing is kept of what it replaced:
        // VISION §6.5 rules complex file versioning out of the MVP.
        Assert.Single(area.OnTheVolume());
    }

    [Fact]
    public async Task Every_write_says_which_version_it_replaces()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);

        // Somebody else got there first.
        var renamed = await area.ChangeFileAsync(stored.Id, "der Plan.md", null, stored.Version, Token);
        Assert.Equal(HttpStatusCode.OK, renamed.Status);

        var stale = await area.ChangeFileAsync(stored.Id, "too late.md", null, stored.Version, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal("stale", stale.Code);
        Assert.NotNull(stale.Body!["updated_at"]);

        // And a write that names no version at all is refused the same way,
        // because the caller's move is the same: read it again and decide.
        using var unguarded = new HttpRequestMessage(HttpMethod.Delete, $"/api/files/{stored.Id}");
        using var response = await area.Owner.SendAsync(unguarded, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task A_folder_cannot_be_put_inside_itself()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var year = await area.MakeFolderAsync("2026", reisen.Id, Token);

        var itself = await area.ChangeFolderAsync(reisen.Id, "Reisen", reisen.Id, reisen.Version, Token);
        Assert.Equal(HttpStatusCode.Conflict, itself.Status);

        // Nor inside something that is already in it, which is the same cycle
        // one step further away.
        var beneath = await area.ChangeFolderAsync(reisen.Id, "Reisen", year.Id, reisen.Version, Token);

        Assert.Equal(HttpStatusCode.Conflict, beneath.Status);
        Assert.Contains("already in it", beneath.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_a_folder_moves_what_is_in_it_without_touching_a_row_of_it()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var year = await area.MakeFolderAsync("2026", reisen.Id, Token);
        var stored = await area.UploadAsync("bahn.pdf", Bytes, year.Id, Token);
        var archive = await area.MakeFolderAsync("Archiv", null, Token);

        var moved = await area.ChangeFolderAsync(reisen.Id, "Reisen", archive.Id, reisen.Version, Token);
        Assert.Equal(HttpStatusCode.OK, moved.Status);

        // The file did not move and its row did not change; its folder's did.
        var read = await area.ReadAsync(stored.Id, Token);

        Assert.Equal(year.Id, read.Body!["folder"]!.GetValue<Guid>());
        Assert.Equal(stored.Body!["updated_at"]!.GetValue<DateTimeOffset>(), read.Body["updated_at"]!.GetValue<DateTimeOffset>());

        var listing = await area.ListAsync(year.Id, Token);

        Assert.Equal(
            ["Archiv", "Reisen", "2026"],
            listing.Body!["chain"]!.AsArray().Select(step => step!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task The_tree_has_a_bottom_and_says_so()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        Guid? parent = null;

        for (var depth = 0; depth < Folder.MaxDepth; depth++)
        {
            var made = await area.MakeFolderAsync($"level {depth}", parent, Token);

            Assert.Equal(HttpStatusCode.Created, made.Status);
            parent = made.Id;
        }

        var refused = await area.MakeFolderAsync("one too far", parent, Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Contains(
            Folder.MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refused.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_that_may_read_files_may_not_change_them()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);
        using var reader = await area.AnAgentReachingAsync("read", Token);

        // Reading, listing and downloading: all three.
        Assert.Equal(HttpStatusCode.OK, (await area.ListAsync(null, Token, reader)).Status);
        Assert.Equal(HttpStatusCode.OK, (await area.ReadAsync(stored.Id, Token, reader)).Status);

        var (status, content, _, _) = await area.DownloadAsync(stored.Id, Token, reader);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Bytes, content);

        // And nothing else.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await area.UploadAsync("other.md", Bytes, null, Token, asWhom: reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await area.ChangeFileAsync(stored.Id, "x.md", null, stored.Version, Token, reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await area.ReplaceAsync(stored.Id, Bytes, stored.Version, Token, asWhom: reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await area.DiscardFileAsync(stored.Id, stored.Version, Token, reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await area.MakeFolderAsync("Reisen", null, Token, reader)).Status);
    }

    [Fact]
    public async Task An_agent_without_files_cannot_reach_one_byte_of_them()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);
        var folder = await area.MakeFolderAsync("Reisen", null, Token);
        using var stranger = await area.AnAgentReachingAsync("none", Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await area.ListAsync(null, Token, stranger)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await area.ReadAsync(stored.Id, Token, stranger)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await area.DiscardFolderAsync(folder.Id, folder.Version, Token, stranger)).Status);

        // The download most of all: a credential that cannot see a file's name
        // must not be able to read its bytes.
        var (status, content, _, _) = await area.DownloadAsync(stored.Id, Token, stranger);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.DoesNotContain(Encoding.UTF8.GetString(Bytes, 32, 16), Encoding.UTF8.GetString(content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_at_all_gets_at_a_file_without_a_credential()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);

        using var stranger = area.Instance.CreateClient();
        using var response = await stranger.GetAsync($"/api/files/{stored.Id}/content", Token);

        // VISION §8: every file retrieval requires authentication, and the MVP
        // has no public content or sharing links.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_switched_off_files_refuses_every_one_of_its_addresses()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);

        await area.SwitchAsync(enabled: false, Token);

        Assert.Equal("disabled", (await area.ListAsync(null, Token)).Code);
        Assert.Equal("disabled", (await area.ReadAsync(stored.Id, Token)).Code);
        Assert.Equal("disabled", (await area.UploadAsync("other.md", Bytes, null, Token)).Code);

        var (status, _, _, _) = await area.DownloadAsync(stored.Id, Token);
        Assert.Equal(HttpStatusCode.Conflict, status);

        // Switched back on, everything is exactly where it was: the switch hides
        // an application, it does not empty it.
        await area.SwitchAsync(enabled: true, Token);

        var (again, content, _, _) = await area.DownloadAsync(stored.Id, Token);

        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Equal(Bytes, content);
    }

    [Fact]
    public async Task An_address_that_is_nothing_says_so_rather_than_answering_an_empty_folder()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var nothing = Guid.CreateVersion7(DateTimeOffset.UtcNow);

        Assert.Equal("not-found", (await area.ListAsync(nothing, Token)).Code);
        Assert.Equal("not-found", (await area.ReadAsync(nothing, Token)).Code);

        var (status, _, _, _) = await area.DownloadAsync(nothing, Token);
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task A_media_type_the_instance_cannot_read_is_stored_as_some_bytes()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("thing", Bytes, null, Token, "text/plain; charset=utf-8");

        // Parameters go: the charset of a file this instance never reads is not
        // a fact it has any business asserting.
        Assert.Equal("text/plain", stored.Body!["media_type"]!.GetValue<string>());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/files/content?name=untyped")
        {
            Content = new ByteArrayContent(Bytes),
        };

        using var response = await area.Owner.SendAsync(request, Token);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/octet-stream", body["media_type"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_bytes_are_at_an_address_made_from_the_id_and_nothing_else()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("Reisekosten 2026.pdf", Bytes, null, Token);

        // The name the owner chose is nowhere on the disk. That is what makes
        // path traversal impossible rather than filtered (VISION §8).
        Assert.Equal([StorageAddress.Of(stored.Id)], area.OnTheVolume());
    }

    [Fact]
    public async Task An_upload_into_a_folder_that_is_not_there_stores_nothing()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var refused = await area.UploadAsync(
            "plan.md", Bytes, Guid.CreateVersion7(DateTimeOffset.UtcNow), Token);

        Assert.Equal(HttpStatusCode.NotFound, refused.Status);
        Assert.Empty(area.OnTheVolume());
    }

    [Fact]
    public async Task A_field_a_request_does_not_define_is_said_out_loud()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        using var refused = await area.Owner.PostAsJsonAsync(
            "/api/files/folders", new { name = "Reisen", parrent = (Guid?)null }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var body = JsonNode.Parse(await refused.Content.ReadAsStringAsync(Token))!;
        Assert.Equal("/problems/unknown-field", body["type"]!.GetValue<string>());
    }
}
