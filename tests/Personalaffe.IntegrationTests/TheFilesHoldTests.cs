using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Files;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// PERSONAL-E6's promises, one test each, end to end against a real Postgres
/// and a real storage volume — the shape <see cref="TheDoorHoldsTests"/> and
/// <see cref="TheSafeguardsHoldTests"/> gave the epics before it.
/// </summary>
/// <remarks>
/// <para>
/// The surfaces are covered in detail elsewhere: <see cref="FilesTests"/> is the
/// API, <see cref="FilesTrashTests"/> the Trash and the sweeps,
/// <c>src/cli/internal/cmd/files_test.go</c> the CLI and
/// <c>src/web/browser/files.spec.ts</c> the browser. What is here is the epic's
/// own list, so that a promise nobody can find a test for is a promise that is
/// not kept.
/// </para>
/// <para>
/// <strong>The two clients are not run against each other here.</strong> They
/// agree because both are generated from <c>docs/api/openapi.json</c> and
/// <c>ContractTests</c> compares that document to what the instance serves;
/// what each does with it is its own suite's. What this proves is the half
/// neither of them can: that the bytes that come out of the instance are the
/// bytes that went into it.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheFilesHoldTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every byte value, twice, with a NUL at each end: anything that treats a
    /// body as text, or stops at a terminator, breaks here rather than on
    /// somebody's photograph.
    /// </summary>
    private static readonly byte[] EveryByte =
        [0, .. Enumerable.Range(0, 256).Select(value => (byte)value),
         .. Enumerable.Range(0, 256).Select(value => (byte)(255 - value)), 0];

    [Fact]
    public async Task A_file_goes_up_gets_organised_and_comes_back_down_with_the_same_bytes()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var year = await area.MakeFolderAsync("2026", reisen.Id, Token);

        var stored = await area.UploadAsync("bahn.pdf", EveryByte, null, Token, "application/pdf");
        Assert.Equal(HttpStatusCode.Created, stored.Status);

        var moved = await area.ChangeFileAsync(
            stored.Id, "Bahn — Reisekosten.pdf", year.Id, stored.Version, Token);
        Assert.Equal(HttpStatusCode.OK, moved.Status);

        var (status, content, _, _) = await area.DownloadAsync(stored.Id, Token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Checksum(EveryByte), Checksum(content));
    }

    [Fact]
    public async Task The_stable_reference_is_the_id_and_a_rename_cannot_break_it()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var folder = await area.MakeFolderAsync("Reisen", null, Token);
        var stored = await area.UploadAsync("draft.pdf", EveryByte, null, Token);

        var address = $"/api/files/{stored.Id}/content";

        var renamed = await area.ChangeFileAsync(stored.Id, "final.pdf", folder.Id, stored.Version, Token);
        Assert.Equal(HttpStatusCode.OK, renamed.Status);

        // The address a Knowledge page would have written down in PERSONAL-E7,
        // asked for as it stands.
        using var again = await area.Owner.GetAsync(address, Token);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(
            Checksum(EveryByte), Checksum(await again.Content.ReadAsByteArrayAsync(Token)));
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("\0")]
    public async Task Nothing_a_caller_can_name_reaches_outside_the_storage_root(string name)
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var refused = await area.UploadAsync(name, EveryByte, null, Token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);

        // Not filtered, but unreachable: an address is made from an id and a
        // name never touches the volume (VISION §8).
        Assert.Empty(area.OnTheVolume());
    }

    [Fact]
    public async Task An_upload_that_stops_halfway_leaves_no_row_and_no_file()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        using var breaking = new StreamContent(new AStreamThatStops(EveryByte, after: 64));
        breaking.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        try
        {
            using var response = await area.Owner.PostAsync(
                "/api/files/content?name=interrupted.bin", breaking, Token);

            // Whatever the instance managed to answer, it must not have been a
            // success: half a file is not a file.
            Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);
        }
        catch (Exception failure) when (failure is IOException or HttpRequestException)
        {
            // The connection going away mid-upload, which is the case this is
            // about. Nothing to assert about the answer there was not one of.
        }

        // No row, because the row is written after the bytes...
        Assert.Empty((await area.ListAsync(null, Token)).Body!["files"]!.AsArray());

        // ...and no bytes either, because the half that arrived is deleted the
        // moment the read fails. What the tidy-up is for is the crash between
        // the two, not this.
        Assert.Empty(area.OnTheVolume());
    }

    [Fact]
    public async Task What_is_stored_is_reachable_only_by_somebody_the_door_let_in()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("private.txt", EveryByte, null, Token);
        var address = $"/api/files/{stored.Id}/content";

        // Nobody at all.
        using var stranger = area.Instance.CreateClient();
        using var unauthenticated = await stranger.GetAsync(address, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        // An agent the owner gave nothing.
        using var none = await area.AnAgentReachingAsync("none", Token);
        using var forbidden = await none.GetAsync(address, Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // And an agent that may read but not write.
        using var reader = await area.AnAgentReachingAsync("read", Token);
        using var allowed = await reader.GetAsync(address, Token);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await area.DiscardFileAsync(stored.Id, stored.Version, Token, reader)).Status);
    }

    [Fact]
    public async Task A_stored_file_is_never_a_document_of_this_instances_own_origin()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var page = Encoding.UTF8.GetBytes("<script>alert(document.cookie)</script>");
        var stored = await area.UploadAsync("trap.html", page, null, Token, "text/html");

        var (status, _, headers, body) = await area.DownloadAsync(stored.Id, Token);

        Assert.Equal(HttpStatusCode.OK, status);

        // An attachment, and no sniffing. A stored page served inline would be
        // script running with the owner's session on the instance's own origin,
        // and the MVP has no previews to lose by refusing (VISION §11).
        Assert.Equal("attachment", body.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Neither_limit_leaves_bytes_behind_when_it_refuses()
    {
        await using var area = await AFileArea.StartedAsync(
            postgres,
            Token,
            new Dictionary<string, string?>
            {
                [StorageSettings.MaxFileVariable] = "1",
                [StorageSettings.MaxTotalVariable] = "2",
            });

        var big = new byte[StorageSettings.Mebibyte + 1];
        var most = new byte[StorageSettings.Mebibyte - 16];

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            (await area.UploadAsync("too-big.bin", big, null, Token)).Status);
        Assert.Empty(area.OnTheVolume());

        Assert.Equal(HttpStatusCode.Created, (await area.UploadAsync("one.bin", most, null, Token)).Status);
        Assert.Equal(HttpStatusCode.Created, (await area.UploadAsync("two.bin", most, null, Token)).Status);

        var full = await area.UploadAsync("three.bin", most, null, Token);

        Assert.Equal(HttpStatusCode.InsufficientStorage, full.Status);
        Assert.Equal("out-of-space", full.Code);

        // Two files stored, and nothing left over from the two that were
        // refused.
        Assert.Equal(2, area.OnTheVolume().Count);
    }

    [Fact]
    public async Task A_deleted_file_is_recoverable_until_its_retention_runs_out_and_then_is_not()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", EveryByte, null, Token);
        await area.DiscardFileAsync(stored.Id, stored.Version, Token);

        // Within the period: in the Trash, with its bytes.
        Assert.Single((await area.TrashAsync(Token))["items"]!.AsArray());
        Assert.Equal([StorageAddress.Of(stored.Id)], area.OnTheVolume());
        Assert.Equal(0, (await area.PurgeAsync(Token)).Total);

        // Past it: the row and the bytes, together.
        await area.BackdateAsync(TimeSpan.FromDays(31), Token);

        Assert.Equal(1, (await area.PurgeAsync(Token)).Total);
        Assert.Empty(area.OnTheVolume());
        Assert.Equal("not-found", (await area.ReadAsync(stored.Id, Token)).Code);
    }

    [Fact]
    public async Task A_folder_and_its_subtree_go_and_come_back_as_one_thing()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var year = await area.MakeFolderAsync("2026", reisen.Id, Token);
        var stored = await area.UploadAsync("bahn.pdf", EveryByte, year.Id, Token);

        await area.DiscardFolderAsync(reisen.Id, reisen.Version, Token);

        var entry = (await area.TrashAsync(Token))["items"]!.AsArray().Single()!;

        // A name taken while it was away is a refusal that offers another, so
        // nobody is ever stuck with something they cannot get out of the Trash.
        await area.MakeFolderAsync("Reisen", null, Token);

        Assert.Equal(HttpStatusCode.Conflict, (await area.RestoreAsync(reisen.Id, Versions.Of(entry), Token)).Status);

        var restored = await area.RestoreAsync(
            reisen.Id, Versions.Of(entry), Token, restoreAs: "Reisen (die alten)");

        Assert.Equal(HttpStatusCode.OK, restored.Status);

        var (status, content, _, _) = await area.DownloadAsync(stored.Id, Token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Checksum(EveryByte), Checksum(content));
    }

    [Fact]
    public async Task A_structural_change_made_from_a_stale_read_is_refused()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var archive = await area.MakeFolderAsync("Archiv", null, Token);

        // Two moves from the same read: the owner in a browser and an agent
        // over the API, which is the ordinary case in this workspace.
        Assert.Equal(
            HttpStatusCode.OK,
            (await area.ChangeFolderAsync(reisen.Id, "Reisen", archive.Id, reisen.Version, Token)).Status);

        var stale = await area.ChangeFolderAsync(reisen.Id, "Reisen", null, reisen.Version, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal("stale", stale.Code);
    }

    [Fact]
    public async Task Switching_files_off_takes_it_out_of_every_surface_and_keeps_everything_in_it()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", EveryByte, null, Token);
        var other = await area.UploadAsync("gone.md", EveryByte, null, Token);

        await area.DiscardFileAsync(other.Id, other.Version, Token);
        await area.SwitchAsync(enabled: false, Token);

        Assert.Equal("disabled", (await area.ListAsync(null, Token)).Code);

        // An aggregate view leaves it out rather than refusing.
        Assert.Empty((await area.TrashAsync(Token))["items"]!.AsArray());

        // And nothing was erased by the switch.
        await area.SwitchAsync(enabled: true, Token);

        Assert.Single((await area.TrashAsync(Token))["items"]!.AsArray());

        var (status, content, _, _) = await area.DownloadAsync(stored.Id, Token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Checksum(EveryByte), Checksum(content));
    }

    [Fact]
    public async Task The_volume_holds_what_the_rows_say_and_nothing_else()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var kept = await area.UploadAsync("kept.md", EveryByte, null, Token);
        var deleted = await area.UploadAsync("deleted.md", EveryByte, null, Token);

        await area.DiscardFileAsync(deleted.Id, deleted.Version, Token);

        // Whatever an instance crashed in the middle of, left over from before.
        var orphan = Path.Combine(
            area.Root,
            StorageAddress.Of(Guid.CreateVersion7(DateTimeOffset.UtcNow))
                .Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        await File.WriteAllBytesAsync(orphan, EveryByte, Token);
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-2));

        Assert.Equal(1, await area.TidyAsync(DateTimeOffset.UtcNow.AddHours(-1), Token));

        // The one that is stored and the one that is in the Trash: both are
        // rows, so both keep their bytes.
        string[] rows = [StorageAddress.Of(kept.Id), StorageAddress.Of(deleted.Id)];

        Assert.Equal(
            rows.Order(StringComparer.Ordinal), area.OnTheVolume().Order(StringComparer.Ordinal));
    }

    private static string Checksum(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

/// <summary>
/// A request body that stops in the middle, the way a connection does.
/// </summary>
/// <remarks>
/// It is a stream and not a killed socket because a killed socket is not a
/// thing a test can arrange reliably, and what is under test is the instance's
/// half: that a read which fails partway leaves neither a row nor bytes.
/// </remarks>
internal sealed class AStreamThatStops(byte[] content, int after) : Stream
{
    private int given;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => given;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (given >= after)
        {
            throw new IOException("The connection went away mid-upload.");
        }

        var taken = Math.Min(Math.Min(count, after - given), content.Length - (given % content.Length));

        Array.Copy(content, given % content.Length, buffer, offset, taken);
        given += taken;

        return taken;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
