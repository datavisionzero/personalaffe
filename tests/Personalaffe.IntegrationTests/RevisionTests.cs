using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// History against a real Postgres: what an edit leaves behind, what recovering
/// one does, and what deletion does to the lot.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RevisionTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Caller TheOwner = Caller.Owner(Guid.CreateVersion7());

    [Fact]
    public async Task An_edit_leaves_the_version_it_replaced_behind()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));
        var page = await things.AddAsync("architecture", parent: null, Token);

        await things.EditAsync(page, await VersionOf(things, page), "the first draft", TheOwner, Token);
        await things.EditAsync(page, await VersionOf(things, page), "the second draft", TheOwner, Token);

        var history = await things.RevisionsAsync(page, Token);

        Assert.Equal(["the first draft", string.Empty], history.Select(revision => revision.Content));
        Assert.Equal("the second draft", await things.ContentAsync(page, Token));
        Assert.All(history, revision => Assert.Equal(CallerKind.Owner, revision.By.Kind));
    }

    [Fact]
    public async Task Recovering_an_old_version_writes_forward_rather_than_rewinding()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));
        var page = await things.AddAsync("architecture", parent: null, Token);

        await things.EditAsync(page, await VersionOf(things, page), "the first draft", TheOwner, Token);
        await things.EditAsync(page, await VersionOf(things, page), "a mistake", TheOwner, Token);

        var theFirstDraft = (await things.RevisionsAsync(page, Token))
            .Single(revision => revision.Content == "the first draft");

        await things.RecoverAsync(page, theFirstDraft.Id, await VersionOf(things, page), TheOwner, Token);

        Assert.Equal("the first draft", await things.ContentAsync(page, Token));

        // The mistake is still recoverable. History only grows, and the
        // recovery is in it — undo is not the one act in this product that
        // destroys work.
        var history = await things.RevisionsAsync(page, Token);

        Assert.Equal(3, history.Count);
        Assert.Equal("a mistake", history[0].Content);
    }

    [Fact]
    public async Task A_recovery_holding_an_older_version_of_the_page_changes_nothing()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));
        var page = await things.AddAsync("architecture", parent: null, Token);

        await things.EditAsync(page, await VersionOf(things, page), "the first draft", TheOwner, Token);

        var theFirstDraft = (await things.RevisionsAsync(page, Token)).Single();
        var readTenMinutesAgo = await VersionOf(things, page);

        // Somebody edits the page between the caller reading it and recovering.
        await things.EditAsync(page, readTenMinutesAgo, "what somebody wrote since", TheOwner, Token);

        var refusal = await Assert.ThrowsAsync<Refusal>(() => things.RecoverAsync(
            page, theFirstDraft.Id, readTenMinutesAgo, TheOwner, Token));

        Assert.Equal(RefusalCode.Stale, refusal.Code);
        Assert.Equal("what somebody wrote since", await things.ContentAsync(page, Token));
    }

    [Fact]
    public async Task Only_what_is_kept_is_kept_and_the_current_content_is_never_among_the_drops()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));
        var page = await things.AddAsync("architecture", parent: null, Token);

        for (var edit = 0; edit <= Revisions.Kept + 5; edit++)
        {
            await things.EditAsync(page, await VersionOf(things, page), $"draft {edit}", TheOwner, Token);
        }

        var history = await things.RevisionsAsync(page, Token);

        Assert.Equal(Revisions.Kept, history.Count);
        Assert.Equal($"draft {Revisions.Kept + 5}", await things.ContentAsync(page, Token));

        // The newest kept revision is the version just before the current one,
        // and the oldest drafts are the ones that went.
        Assert.Equal($"draft {Revisions.Kept + 4}", history[0].Content);
        Assert.DoesNotContain("draft 0", history.Select(revision => revision.Content));
    }

    [Fact]
    public async Task Deleting_keeps_the_history_and_removing_for_good_takes_it()
    {
        var things = new Things(await ProvingGround.PreparedAsync(postgres));
        var page = await things.AddAsync("architecture", parent: null, Token);

        await things.EditAsync(page, await VersionOf(things, page), "the first draft", TheOwner, Token);
        await things.DeleteAsync(page, TheOwner, Token);

        // In the Trash, its history goes with it and comes back with it.
        Assert.Single(await things.RevisionsAsync(page, Token));

        var entry = await things.InTheTrashAsync(page, Token);
        await things.RestoreAsync(page, entry.Version, restoreAs: null, Token);
        Assert.Single(await things.RevisionsAsync(page, Token));

        await things.DeleteAsync(page, TheOwner, Token);
        await things.RemoveAsync(page, Token);

        // Removed for good, and nothing outlives the page it is a version of.
        Assert.Empty(await things.RevisionsAsync(page, Token));
    }

    private static async Task<ContentVersion> VersionOf(Things things, Guid id) =>
        (await things.InTheTrashAsync(id, Token)).Version;
}
