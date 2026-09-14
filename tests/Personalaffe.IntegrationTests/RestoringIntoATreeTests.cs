using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The restore rules against a real tree in a real Postgres: what comes back
/// with what, what is in the way, and where something goes when the place it
/// came from has been removed for good.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RestoringIntoATreeTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Caller TheOwner = Caller.Owner(Guid.CreateVersion7());

    [Fact]
    public async Task Deleting_a_folder_takes_its_subtree_and_restoring_it_brings_the_whole_thing_back()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));

        var notes = await things.AddAsync("notes", parent: null, Token);
        var architecture = await things.AddAsync("architecture", notes, Token);
        var deeper = await things.AddAsync("storage", architecture, Token);
        await things.AddAsync("groceries", parent: null, Token);

        await things.DeleteAsync(notes, TheOwner, Token);

        Assert.Equal(["/groceries"], await things.PathsAsync(Token));

        var entry = await things.InTheTrashAsync(notes, Token);
        await things.RestoreAsync(notes, entry.Version, restoreAs: null, Token);

        Assert.Equal(
            ["/groceries", "/notes", "/notes/architecture", "/notes/architecture/storage"],
            await things.PathsAsync(Token));

        Assert.False((await things.InTheTrashAsync(deeper, Token)).IsDeleted());
    }

    [Fact]
    public async Task Restoring_a_page_brings_back_the_folders_it_needs_and_nothing_else()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));

        var work = await things.AddAsync("work", parent: null, Token);
        var notes = await things.AddAsync("notes", work, Token);
        var architecture = await things.AddAsync("architecture", notes, Token);
        var groceries = await things.AddAsync("groceries", notes, Token);

        // The page goes first, on its own; the folder above it goes later, with
        // everything still in it.
        await things.DeleteAsync(architecture, TheOwner, Token);
        await things.DeleteAsync(notes, TheOwner, Token);

        Assert.Equal(["/work"], await things.PathsAsync(Token));

        var entry = await things.InTheTrashAsync(architecture, Token);
        await things.RestoreAsync(architecture, entry.Version, restoreAs: null, Token);

        // `notes` comes back because the page would otherwise be somewhere the
        // owner cannot reach. `groceries` does not: it was deleted with the
        // folder and is not on the way to anything being restored.
        Assert.Equal(["/work", "/work/notes", "/work/notes/architecture"], await things.PathsAsync(Token));
        Assert.True((await things.InTheTrashAsync(groceries, Token)).IsDeleted());
    }

    [Fact]
    public async Task A_name_that_has_been_taken_since_is_refused_and_the_retry_succeeds()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));

        var notes = await things.AddAsync("notes", parent: null, Token);
        var architecture = await things.AddAsync("architecture", notes, Token);

        await things.DeleteAsync(architecture, TheOwner, Token);

        // Somebody wrote a new page with the same name while the old one was in
        // the Trash, which is exactly how this happens in a workspace.
        await things.AddAsync("architecture", notes, Token);

        var entry = await things.InTheTrashAsync(architecture, Token);

        var refusal = await Assert.ThrowsAsync<Refusal>(
            () => things.RestoreAsync(architecture, entry.Version, restoreAs: null, Token));

        Assert.Equal(RefusalCode.Conflict, refusal.Code);
        Assert.Equal(["/notes", "/notes/architecture"], await things.PathsAsync(Token));

        var restored = await things.RestoreAsync(
            architecture, entry.Version, restoreAs: "architecture (2026)", Token);

        Assert.Equal("architecture (2026)", restored.Name);
        Assert.Equal(
            ["/notes", "/notes/architecture", "/notes/architecture (2026)"],
            await things.PathsAsync(Token));
    }

    [Fact]
    public async Task Something_whose_folder_has_expired_goes_to_the_root_and_says_so()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));

        var notes = await things.AddAsync("notes", parent: null, Token);
        var architecture = await things.AddAsync("architecture", notes, Token);

        // The page goes first, on its own. The folder goes later and does not
        // take the page with it, because the page is already gone. Then the
        // owner removes the folder from the Trash for good, which is the one
        // thing that can leave a chain broken.
        await things.DeleteAsync(architecture, TheOwner, Token);
        await things.DeleteAsync(notes, TheOwner, Token);

        Assert.Equal(1, await things.RemoveAsync(notes, Token));

        var entry = await things.InTheTrashAsync(architecture, Token);
        var restored = await things.RestoreAsync(architecture, entry.Version, restoreAs: null, Token);

        Assert.True(restored.MovedToTheRoot);
        Assert.Null(restored.Where);
        Assert.Equal(["/architecture"], await things.PathsAsync(Token));
    }

    [Fact]
    public async Task A_restore_that_is_holding_an_older_version_changes_nothing()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));

        var architecture = await things.AddAsync("architecture", parent: null, Token);
        await things.DeleteAsync(architecture, TheOwner, Token);

        var stale = ContentVersion.Of((await things.InTheTrashAsync(architecture, Token))
            .UpdatedAt.AddMinutes(-1));

        var refusal = await Assert.ThrowsAsync<Refusal>(
            () => things.RestoreAsync(architecture, stale, restoreAs: null, Token));

        Assert.Equal(RefusalCode.Stale, refusal.Code);
        Assert.Empty(await things.PathsAsync(Token));
    }
}
