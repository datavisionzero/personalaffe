using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Deleting lasting content sets it aside: it leaves every ordinary read, keeps
/// its row, and still says who put it there.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RecoverableDeletionTests(PostgresFixture postgres)
{
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_deleted_row_is_absent_from_a_read_that_never_mentions_deletion()
    {
        var open = await ProvingGround.PreparedAsync(postgres);
        var (kept, deleted) = await TwoThings(open);

        await using var reading = open();

        // Not one of these says a word about deletion, which is exactly the
        // point: the filter is what a module gets for free, and forgetting it
        // is what it makes impossible.
        Assert.Equal(1, await reading.Things.CountAsync(Token));
        Assert.Null(await reading.Things.FirstOrDefaultAsync(t => t.Id == deleted, Token));
        Assert.NotNull(await reading.Things.FirstOrDefaultAsync(t => t.Id == kept, Token));
        Assert.DoesNotContain(deleted, await reading.Things.Select(t => t.Id).ToListAsync(Token));
    }

    [Fact]
    public async Task The_row_is_still_there_for_the_one_read_that_asks_for_it()
    {
        var open = await ProvingGround.PreparedAsync(postgres);
        var (_, deleted) = await TwoThings(open);

        await using var reading = open();
        var inTheTrash = await reading.Things
            .IgnoreQueryFilters()
            .SingleAsync(thing => thing.Id == deleted, Token);

        Assert.True(inTheTrash.IsDeleted());
        Assert.NotNull(inTheTrash.DeletedAt);
    }

    [Fact]
    public async Task A_trash_entry_names_the_agent_access_that_deleted_it_with_nothing_to_look_up()
    {
        var open = await ProvingGround.PreparedAsync(postgres);
        var (access, _) = AgentAccess.Grant(
            "the laptop agent",
            new Permissions(Permission.ReadWrite, Permission.ReadWrite, Permission.None, Permission.None),
            DateTimeOffset.UtcNow);

        var id = Guid.CreateVersion7();

        await using (var deleting = open())
        {
            var now = DateTimeOffset.UtcNow;
            var thing = new Thing { Id = id, Name = "the page", CreatedAt = now, UpdatedAt = now };

            thing.Delete(Caller.Agent(access), now);
            deleting.Things.Add(thing);
            await deleting.SaveChangesAsync(Token);
        }

        await using var reading = open();
        var entry = await reading.Things
            .IgnoreQueryFilters()
            .SingleAsync(thing => thing.Id == id, Token);

        // Nothing joins to an agent_access row, and there is no such table in
        // this database at all. The name is a copy, which is what makes it
        // survive the access being revoked and its row being gone.
        Assert.NotNull(entry.DeletedBy);
        Assert.Equal(CallerKind.Agent, entry.DeletedBy.Kind);
        Assert.Equal("the laptop agent", entry.DeletedBy.Name);
        Assert.Contains("the laptop agent", entry.DeletedBy.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restoring_puts_it_back_into_every_ordinary_read()
    {
        var open = await ProvingGround.PreparedAsync(postgres);
        var (_, deleted) = await TwoThings(open);

        await using (var restoring = open())
        {
            var entry = await restoring.Things
                .IgnoreQueryFilters()
                .SingleAsync(thing => thing.Id == deleted, Token);

            entry.Restore();
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await GuardedSave.SaveAsync(restoring, "The page", Token);
        }

        await using var reading = open();

        Assert.Equal(2, await reading.Things.CountAsync(Token));
        Assert.Null((await reading.Things.SingleAsync(t => t.Id == deleted, Token)).DeletedBy);
    }

    [Fact]
    public async Task Restoring_is_guarded_like_every_other_write()
    {
        var open = await ProvingGround.PreparedAsync(postgres);
        var (_, deleted) = await TwoThings(open);

        await using var first = open();
        await using var second = open();

        var read = await first.Things.IgnoreQueryFilters().SingleAsync(t => t.Id == deleted, Token);
        var alsoRead = await second.Things.IgnoreQueryFilters().SingleAsync(t => t.Id == deleted, Token);

        read.Restore();
        read.UpdatedAt = DateTimeOffset.UtcNow;
        await GuardedSave.SaveAsync(first, "The page", Token);

        alsoRead.Restore();
        alsoRead.UpdatedAt = DateTimeOffset.UtcNow;

        var refusal = await Assert.ThrowsAsync<Refusal>(
            () => GuardedSave.SaveAsync(second, "The page", Token));

        Assert.Equal(RefusalCode.Stale, refusal.Code);
    }

    [Fact]
    public void A_deleted_address_answers_that_it_can_still_be_brought_back()
    {
        var deletedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var thing = new Thing { Id = Guid.CreateVersion7(), Name = "the page", CreatedAt = deletedAt };

        thing.Delete(Caller.Owner(Guid.CreateVersion7()), deletedAt);

        var refusal = thing.Gone("The page", ThirtyDays);

        Assert.Equal(RefusalCode.Deleted, refusal.Code);
        Assert.Equal(deletedAt, refusal.Extensions["deleted_at"]);
        Assert.Equal(deletedAt + ThirtyDays, refusal.Extensions["expires_at"]);
        Assert.Contains("the owner", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_what_is_already_deleted_does_not_rewrite_who_deleted_it()
    {
        var first = DateTimeOffset.UtcNow.AddDays(-2);
        var thing = new Thing { Id = Guid.CreateVersion7(), Name = "the page", CreatedAt = first };
        var (agent, _) = AgentAccess.Grant("the laptop agent", Permissions.None, first);

        thing.Delete(Caller.Agent(agent), first);
        thing.Delete(Caller.Owner(Guid.CreateVersion7()), first.AddDays(1));

        Assert.Equal(first, thing.DeletedAt);
        Assert.Equal("the laptop agent", thing.DeletedBy?.Name);
    }

    private async Task<(Guid Kept, Guid Deleted)> TwoThings(Func<ProvingGround> open)
    {
        var now = DateTimeOffset.UtcNow;
        var kept = new Thing { Id = Guid.CreateVersion7(), Name = "kept", CreatedAt = now, UpdatedAt = now };
        var gone = new Thing { Id = Guid.CreateVersion7(), Name = "gone", CreatedAt = now, UpdatedAt = now };

        gone.Delete(Caller.Owner(Guid.CreateVersion7()), now);

        await using var context = open();
        context.Things.AddRange(kept, gone);
        await context.SaveChangesAsync(Token);

        return (kept.Id, gone.Id);
    }
}
