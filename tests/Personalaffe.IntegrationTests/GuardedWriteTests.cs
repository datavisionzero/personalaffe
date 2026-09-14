using Microsoft.EntityFrameworkCore;
using Personalaffe.Api.Http;
using Personalaffe.Domain;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Two writes from the same starting point, against a real Postgres: one of
/// them changes the object and the other is refused.
/// </summary>
/// <remarks>
/// This is the case the guard exists for, and the only one a substitute cannot
/// vouch for — both writes read the same version, both pass the check an act
/// makes before it changes anything, and what separates them is the
/// <c>where updated_at = …</c> the database applies.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class GuardedWriteTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_holding_the_version_it_read_changes_the_object()
    {
        var open = await ProvingGround.PreparedAsync(postgres);
        var id = await AThing(open, "the page");

        await using (var context = open())
        {
            var thing = await context.Things.SingleAsync(candidate => candidate.Id == id, Token);

            EntityTags.RequireCurrent(thing.Version, thing.Version, "The page");
            thing.Content = "what the owner wrote";
            thing.UpdatedAt = DateTimeOffset.UtcNow;

            await GuardedSave.SaveAsync(context, "The page", Token);
        }

        await using var after = open();
        var stored = await after.Things.SingleAsync(candidate => candidate.Id == id, Token);

        Assert.Equal("what the owner wrote", stored.Content);
    }

    [Fact]
    public async Task Of_two_writes_that_read_the_same_version_only_the_first_one_wins()
    {
        var open = await ProvingGround.PreparedAsync(postgres);
        var id = await AThing(open, "the page");

        // Two callers, each on its own connection, each having read the object
        // before the other wrote. Neither can tell, and the check both of them
        // make passes.
        await using var owner = open();
        await using var agent = open();

        var read = await owner.Things.SingleAsync(candidate => candidate.Id == id, Token);
        var alsoRead = await agent.Things.SingleAsync(candidate => candidate.Id == id, Token);

        Assert.True(read.Version.Matches(alsoRead.Version));

        read.Content = "what the owner wrote";
        read.UpdatedAt = DateTimeOffset.UtcNow;
        await GuardedSave.SaveAsync(owner, "The page", Token);

        alsoRead.Content = "what the agent wrote";
        alsoRead.UpdatedAt = DateTimeOffset.UtcNow;

        var refusal = await Assert.ThrowsAsync<Refusal>(
            () => GuardedSave.SaveAsync(agent, "The page", Token));

        Assert.Equal(RefusalCode.Stale, refusal.Code);

        await using var after = open();
        var stored = await after.Things.SingleAsync(candidate => candidate.Id == id, Token);

        Assert.Equal("what the owner wrote", stored.Content);
    }

    [Fact]
    public async Task A_version_survives_the_round_trip_through_postgres_unchanged()
    {
        var open = await ProvingGround.PreparedAsync(postgres);

        // Straight from the clock, with every one of its seven fractional
        // digits. What comes back has six, and the guard has to call the two
        // the same version or nothing in the product could ever be written
        // twice.
        var written = DateTimeOffset.UtcNow;
        var id = Guid.CreateVersion7();

        await using (var context = open())
        {
            context.Things.Add(
                new Thing { Id = id, Name = "the page", CreatedAt = written, UpdatedAt = written });
            await context.SaveChangesAsync(Token);
        }

        await using var reading = open();
        var stored = await reading.Things.SingleAsync(candidate => candidate.Id == id, Token);

        Assert.True(ContentVersion.Of(written).Matches(stored.Version));
        Assert.Equal(EntityTags.For(ContentVersion.Of(written)), EntityTags.For(stored.Version));
    }

    private static async Task<Guid> AThing(Func<ProvingGround> open, string name)
    {
        var id = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await using var context = open();
        context.Things.Add(new Thing { Id = id, Name = name, CreatedAt = now, UpdatedAt = now });
        await context.SaveChangesAsync(Token);

        return id;
    }
}
