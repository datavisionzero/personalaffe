using System.Net;
using System.Text.Json.Nodes;
using Personalaffe.Domain.Files;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The Trash with something in it at last: files and folders that leave every
/// ordinary read, come back where they were, and are swept away with their
/// bytes when their time runs out.
/// </summary>
/// <remarks>
/// PERSONAL-E3 wrote these rules against a proving ground nobody could reach.
/// This is the first module that applies them to content an owner has, so what
/// is under test here is <c>Recoverable</c>, <c>Restoration</c> and
/// <c>ITrash</c> as much as it is Files.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class FilesTrashTests(PostgresFixture postgres)
{
    private static readonly byte[] Bytes = "the owner's own bytes"u8.ToArray();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_deleted_file_leaves_every_ordinary_read_and_says_it_can_come_back()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);

        Assert.Equal(HttpStatusCode.NoContent, (await area.DiscardFileAsync(stored.Id, stored.Version, Token)).Status);

        var listing = await area.ListAsync(null, Token);
        Assert.Empty(listing.Body!["files"]!.AsArray());

        // `deleted` and not `not-found`: both are 404 and one of them means the
        // owner can have the thing back (docs/api.md).
        var gone = await area.ReadAsync(stored.Id, Token);

        Assert.Equal(HttpStatusCode.NotFound, gone.Status);
        Assert.Equal("deleted", gone.Code);
        Assert.NotNull(gone.Body!["deleted_at"]);
        Assert.NotNull(gone.Body["expires_at"]);

        var trash = await area.TrashAsync(Token);
        var entry = trash["items"]!.AsArray().Single()!;

        Assert.Equal("files", entry["application"]!.GetValue<string>());
        Assert.Equal("plan.md", entry["name"]!.GetValue<string>());
        Assert.Equal("owner", entry["deleted_by"]!["kind"]!.GetValue<string>());

        // The bytes are still there. Restoring a file that gave back an empty
        // one would be a Trash nobody could rely on.
        Assert.Equal([StorageAddress.Of(stored.Id)], area.OnTheVolume());
    }

    [Fact]
    public async Task Restoring_a_file_puts_it_back_where_it_came_from()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var folder = await area.MakeFolderAsync("Reisen", null, Token);
        var stored = await area.UploadAsync("bahn.pdf", Bytes, folder.Id, Token);

        await area.DiscardFileAsync(stored.Id, stored.Version, Token);

        var entry = (await area.TrashAsync(Token))["items"]!.AsArray().Single()!;
        var restored = await area.RestoreAsync(
            stored.Id, Versions.Of(entry), Token);

        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.Equal("bahn.pdf", restored.Body!["name"]!.GetValue<string>());
        Assert.Equal("Reisen", restored.Body["where"]!.GetValue<string>());
        Assert.False(restored.Body["moved_to_the_root"]!.GetValue<bool>());

        var (status, content, _, _) = await area.DownloadAsync(stored.Id, Token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Bytes, content);
    }

    [Fact]
    public async Task A_name_taken_while_it_was_in_the_trash_is_a_conflict_and_never_a_silent_rename()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);
        await area.DiscardFileAsync(stored.Id, stored.Version, Token);

        // Something else took the name while it was away.
        await area.UploadAsync("Plan.md", Bytes, null, Token);

        var entry = (await area.TrashAsync(Token))["items"]!.AsArray().Single()!;
        var version = Versions.Of(entry);

        var refused = await area.RestoreAsync(stored.Id, version, Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("conflict", refused.Code);

        // The same call takes the name to put it back under, so nobody is ever
        // stuck with something they cannot get out of the Trash.
        var restored = await area.RestoreAsync(stored.Id, version, Token, restoreAs: "plan (the older one).md");

        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.Equal("plan (the older one).md", restored.Body!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Deleting_a_folder_is_one_trash_entry_and_it_all_comes_back_together()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var year = await area.MakeFolderAsync("2026", reisen.Id, Token);
        var bahn = await area.UploadAsync("bahn.pdf", Bytes, year.Id, Token);
        var hotel = await area.UploadAsync("hotel.pdf", Bytes, reisen.Id, Token);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await area.DiscardFolderAsync(reisen.Id, reisen.Version, Token)).Status);

        // One entry: the folder the owner deleted. Not four things they have to
        // put back one at a time.
        var items = (await area.TrashAsync(Token))["items"]!.AsArray();

        Assert.Single(items);
        Assert.Equal("Reisen", items[0]!["name"]!.GetValue<string>());

        Assert.Empty((await area.ListAsync(null, Token)).Body!["folders"]!.AsArray());

        var restored = await area.RestoreAsync(reisen.Id, Versions.Of(items[0]!), Token);
        Assert.Equal(HttpStatusCode.OK, restored.Status);

        var top = await area.ListAsync(reisen.Id, Token);

        Assert.Equal(["2026"], top.Body!["folders"]!.AsArray().Select(one => one!["name"]!.GetValue<string>()));
        Assert.Equal(["hotel.pdf"], top.Body["files"]!.AsArray().Select(one => one!["name"]!.GetValue<string>()));

        var (status, content, _, _) = await area.DownloadAsync(bahn.Id, Token);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Bytes, content);

        Assert.Equal(HttpStatusCode.OK, (await area.ReadAsync(hotel.Id, Token)).Status);
    }

    [Fact]
    public async Task What_the_owner_deleted_separately_keeps_its_own_entry_and_its_own_expiry()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var bahn = await area.UploadAsync("bahn.pdf", Bytes, reisen.Id, Token);
        var hotel = await area.UploadAsync("hotel.pdf", Bytes, reisen.Id, Token);

        // One file deleted on its own, and then the folder around it.
        await area.DiscardFileAsync(bahn.Id, bahn.Version, Token);
        await area.DiscardFolderAsync(reisen.Id, reisen.Version, Token);

        var items = (await area.TrashAsync(Token))["items"]!.AsArray();

        Assert.Equal(
            ["Reisen", "bahn.pdf"],
            items.Select(item => item!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));

        var reisenEntry = items.First(item => item!["name"]!.GetValue<string>() == "Reisen")!;

        Assert.Equal(HttpStatusCode.OK, (await area.RestoreAsync(reisen.Id, Versions.Of(reisenEntry), Token)).Status);

        // The folder and what went with it are back; the file the owner deleted
        // on purpose is not.
        var listing = await area.ListAsync(reisen.Id, Token);

        Assert.Equal(["hotel.pdf"], listing.Body!["files"]!.AsArray().Select(one => one!["name"]!.GetValue<string>()));
        Assert.Equal("deleted", (await area.ReadAsync(bahn.Id, Token)).Code);
        Assert.Equal(HttpStatusCode.OK, (await area.ReadAsync(hotel.Id, Token)).Status);
    }

    [Fact]
    public async Task Removing_a_folder_for_good_does_not_destroy_what_was_deleted_separately()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var bahn = await area.UploadAsync("bahn.pdf", Bytes, reisen.Id, Token);
        var hotel = await area.UploadAsync("hotel.pdf", Bytes, reisen.Id, Token);

        await area.DiscardFileAsync(bahn.Id, bahn.Version, Token);
        await area.DiscardFolderAsync(reisen.Id, reisen.Version, Token);

        var items = (await area.TrashAsync(Token))["items"]!.AsArray();
        var reisenEntry = items.First(item => item!["name"]!.GetValue<string>() == "Reisen")!;

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await area.RemoveForGoodAsync(reisen.Id, Versions.Of(reisenEntry), Token)).Status);

        // `bahn.pdf` was a Trash entry of its own and is still one — with its
        // bytes. Destroying it as a side effect would destroy something nobody
        // selected.
        var left = (await area.TrashAsync(Token))["items"]!.AsArray();

        Assert.Single(left);
        Assert.Equal("bahn.pdf", left[0]!["name"]!.GetValue<string>());
        Assert.Equal([StorageAddress.Of(bahn.Id)], area.OnTheVolume());

        // What did go, went with its bytes.
        Assert.DoesNotContain(StorageAddress.Of(hotel.Id), area.OnTheVolume());
    }

    [Fact]
    public async Task Something_whose_folder_is_gone_for_good_comes_back_at_the_top_and_says_so()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var stored = await area.UploadAsync("bahn.pdf", Bytes, reisen.Id, Token);

        // The file first, on its own, so that it is an entry of its own.
        await area.DiscardFileAsync(stored.Id, stored.Version, Token);

        var fileEntry = (await area.TrashAsync(Token))["items"]!.AsArray().Single()!;

        // Then the folder, and then the folder for good.
        var folderVersion = (await area.ListAsync(null, Token)).Body!["folders"]!.AsArray()
            .Single()!["updated_at"]!.GetValue<DateTimeOffset>();

        await area.DiscardFolderAsync(reisen.Id, Versions.For(folderVersion), Token);

        var folderEntry = (await area.TrashAsync(Token))["items"]!.AsArray()
            .First(item => item!["name"]!.GetValue<string>() == "Reisen")!;

        await area.RemoveForGoodAsync(reisen.Id, Versions.Of(folderEntry), Token);

        var restored = await area.RestoreAsync(stored.Id, Versions.Of(fileEntry), Token);

        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.True(restored.Body!["moved_to_the_root"]!.GetValue<bool>());
        Assert.Null(restored.Body["where"]?.GetValue<string>());

        // Nothing in this product moves the owner's content without saying so.
        Assert.Equal(
            ["bahn.pdf"],
            (await area.ListAsync(null, Token)).Body!["files"]!.AsArray()
                .Select(one => one!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task An_ancestor_in_the_trash_comes_back_with_what_needs_it_and_as_itself()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var reisen = await area.MakeFolderAsync("Reisen", null, Token);
        var year = await area.MakeFolderAsync("2026", reisen.Id, Token);
        var bahn = await area.UploadAsync("bahn.pdf", Bytes, year.Id, Token);
        var hotel = await area.UploadAsync("hotel.pdf", Bytes, reisen.Id, Token);

        // The file on its own, then everything above it.
        await area.DiscardFileAsync(bahn.Id, bahn.Version, Token);
        await area.DiscardFolderAsync(reisen.Id, reisen.Version, Token);

        var fileEntry = (await area.TrashAsync(Token))["items"]!.AsArray()
            .First(item => item!["name"]!.GetValue<string>() == "bahn.pdf")!;

        var restored = await area.RestoreAsync(bahn.Id, Versions.Of(fileEntry), Token);

        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.Equal("2026", restored.Body!["where"]!.GetValue<string>());

        // Both folders came back, because the file would otherwise be
        // unreachable. `hotel.pdf` did not: an ancestor comes back as itself and
        // not with everything it used to contain.
        Assert.Equal(
            ["bahn.pdf"],
            (await area.ListAsync(year.Id, Token)).Body!["files"]!.AsArray()
                .Select(one => one!["name"]!.GetValue<string>()));

        Assert.Empty((await area.ListAsync(reisen.Id, Token)).Body!["files"]!.AsArray());
        Assert.Equal("deleted", (await area.ReadAsync(hotel.Id, Token)).Code);
    }

    [Fact]
    public async Task The_sweep_takes_the_row_and_the_bytes_and_running_it_twice_takes_nothing()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var kept = await area.UploadAsync("kept.md", Bytes, null, Token);
        var going = await area.UploadAsync("going.md", Bytes, null, Token);

        await area.DiscardFileAsync(going.Id, going.Version, Token);
        await area.BackdateAsync(TimeSpan.FromDays(31), Token);

        var swept = await area.PurgeAsync(Token);

        Assert.True(swept.Swept);
        Assert.Empty(swept.Failed);
        Assert.Equal(1, swept.Total);

        // The row and the file, together. A Trash whose expiry left the bytes
        // behind would be a quota that only ever grows.
        Assert.Equal([StorageAddress.Of(kept.Id)], area.OnTheVolume());
        Assert.Empty((await area.TrashAsync(Token))["items"]!.AsArray());
        Assert.Equal("not-found", (await area.ReadAsync(going.Id, Token)).Code);

        // The sweep has to be able to run again after being interrupted, so
        // running it when there is nothing to do is not a failure.
        var again = await area.PurgeAsync(Token);

        Assert.True(again.Swept);
        Assert.Equal(0, again.Total);
    }

    [Fact]
    public async Task The_sweep_runs_while_files_is_switched_off()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("going.md", Bytes, null, Token);

        await area.DiscardFileAsync(stored.Id, stored.Version, Token);
        await area.BackdateAsync(TimeSpan.FromDays(31), Token);
        await area.SwitchAsync(enabled: false, Token);

        var swept = await area.PurgeAsync(Token);

        // Switching an application off hides it. It does not suspend a deadline
        // the owner set by deleting something a month ago, and it does not
        // silently erase anything either (PERSONAL-E4).
        Assert.Equal(1, swept.Total);
        Assert.Empty(area.OnTheVolume());
    }

    [Fact]
    public async Task Switching_files_off_erases_nothing_that_has_not_expired()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("going.md", Bytes, null, Token);
        await area.DiscardFileAsync(stored.Id, stored.Version, Token);
        await area.SwitchAsync(enabled: false, Token);

        Assert.Equal(0, (await area.PurgeAsync(Token)).Total);

        await area.SwitchAsync(enabled: true, Token);

        Assert.Single((await area.TrashAsync(Token))["items"]!.AsArray());
    }

    [Fact]
    public async Task The_tidy_up_removes_what_nothing_points_at_and_nothing_else()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);

        // What an instance killed between the bytes and the row leaves behind,
        // and what an upload whose connection went away leaves behind.
        var orphan = Guid.CreateVersion7(DateTimeOffset.UtcNow);
        var orphanPath = Path.Combine(area.Root, StorageAddress.Of(orphan).Replace('/', Path.DirectorySeparatorChar));
        var partPath = Path.Combine(
            area.Root, StorageAddress.Arriving(Guid.NewGuid()).Replace('/', Path.DirectorySeparatorChar));

        // And something an operator put there, which is not ours to remove.
        var theirs = Path.Combine(area.Root, StorageAddress.Files, "a note from the operator.txt");

        foreach (var path in (string[])[orphanPath, partPath, theirs])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, Bytes, Token);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
        }

        var removed = await area.TidyAsync(DateTimeOffset.UtcNow.AddHours(-1), Token);

        Assert.Equal(2, removed);
        Assert.False(File.Exists(orphanPath));
        Assert.False(File.Exists(partPath));

        // The owner's file, and the operator's, are both still there.
        Assert.True(File.Exists(theirs));
        Assert.Contains(StorageAddress.Of(stored.Id), area.OnTheVolume());
    }

    [Fact]
    public async Task The_tidy_up_leaves_an_upload_that_may_still_be_arriving_alone()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var arriving = Path.Combine(
            area.Root, StorageAddress.Arriving(Guid.NewGuid()).Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(arriving)!);
        await File.WriteAllBytesAsync(arriving, Bytes, Token);

        // Written just now, which is what an upload in flight looks like from
        // outside: a file nothing points at yet.
        Assert.Equal(0, await area.TidyAsync(DateTimeOffset.UtcNow.AddHours(-1), Token));
        Assert.True(File.Exists(arriving));
    }

    [Fact]
    public async Task A_file_in_the_trash_keeps_its_bytes_and_counts_towards_the_quota()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);

        await area.DiscardFileAsync(stored.Id, stored.Version, Token);

        // Its bytes are still on the volume, so they are still being used. A
        // quota that ignored the Trash would be one an owner could walk past by
        // deleting and uploading in turn — and then find they could not restore
        // what they deleted.
        Assert.Equal(
            Bytes.Length,
            (await area.ListAsync(null, Token)).Body!["used_bytes"]!.GetValue<long>());

        // And the tidy-up leaves them alone, because the row is still there.
        Assert.Equal(0, await area.TidyAsync(DateTimeOffset.UtcNow.AddHours(1), Token));
        Assert.Equal([StorageAddress.Of(stored.Id)], area.OnTheVolume());
    }

    [Fact]
    public async Task An_agent_may_delete_and_restore_and_may_not_remove_for_good()
    {
        await using var area = await AFileArea.StartedAsync(postgres, Token);

        var stored = await area.UploadAsync("plan.md", Bytes, null, Token);
        using var agent = await area.AnAgentReachingAsync("read_write", Token);

        // Write access includes deletion, and for lasting content that is into
        // the Trash (docs/api.md, What an agent may do).
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await area.DiscardFileAsync(stored.Id, stored.Version, Token, agent)).Status);

        var entry = (await area.TrashAsync(Token))["items"]!.AsArray().Single()!;

        // What an agent may not do is make a deletion permanent, which is the
        // whole reason the Trash keeps an agent from destroying the owner's
        // content.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await area.RemoveForGoodAsync(stored.Id, Versions.Of(entry), Token, agent)).Status);

        Assert.Equal([StorageAddress.Of(stored.Id)], area.OnTheVolume());
    }
}

/// <summary>
/// The version an item in a list carries, as a write sends it back. A list
/// cannot answer an <c>ETag</c> per item, so the value travels in the item
/// (<c>docs/api.md</c>, The guarded write).
/// </summary>
internal static class Versions
{
    internal static string Of(JsonNode item) => For(item["updated_at"]!.GetValue<DateTimeOffset>());

    internal static string For(DateTimeOffset updatedAt) =>
        Personalaffe.Api.Http.EntityTags.For(Personalaffe.Domain.ContentVersion.Of(updatedAt));
}
