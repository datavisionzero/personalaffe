using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarkModelTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upgrade_keeps_existing_agents_without_access_to_saved_links()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var context = AnInstance.ContextFor(connection);
        await context.GetService<IMigrator>().MigrateAsync("20260919155108_TheInstanceAppearance", Token);
        await context.Database.ExecuteSqlRawAsync("""
            insert into agent_access
                (id, name, token_prefix, token_hash, token_issued_at, created_at, updated_at,
                 files, knowledge, scratchpad, tasks)
            values ('01996000-0000-7000-8000-000000000001', 'Existing agent', 'test', decode('01', 'hex'),
                    now(), now(), now(), 'read_write', 'read_write', 'read_write', 'read_write')
            """, Token);
        await AnInstance.MigratorFor(context).ApplyAsync(Token);
        var access = await context.AgentAccess.SingleAsync(Token);
        Assert.Equal(Permission.None, access.Permissions.Bookmarks);
        Assert.Equal(Permission.ReadWrite, access.Permissions.Files);
        Assert.True((await context.Applications.SingleAsync(row => row.Application == WorkspaceApplication.Bookmarks, Token)).Enabled);
    }

    [Fact]
    public async Task Saved_links_keep_their_order_version_and_recoverable_state_after_restart()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var folder = BookmarkFolder.Make("Private links", null, true, now);
        var bookmark = Bookmark.Make("A link", "https://example.com/", null, folder.Id, now);
        bookmark.FavoriteAt(42.5, now.AddSeconds(1));
        await using (var context = AnInstance.ContextFor(connection))
        {
            await AnInstance.MigratorFor(context).ApplyAsync(Token);
            context.BookmarkFolders.Add(folder);
            context.Bookmarks.Add(bookmark);
            await context.SaveChangesAsync(Token);
        }
        await using (var context = AnInstance.ContextFor(connection))
        {
            var read = await context.Bookmarks.SingleAsync(Token);
            Assert.Equal(bookmark.Id, read.Id);
            Assert.Equal(42.5, read.FavoritePosition);
            Assert.Equal(bookmark.Version, read.Version);
            Assert.True((await context.BookmarkFolders.SingleAsync(Token)).Private);
            read.DeletedAt = now.AddSeconds(2);
            read.PreservePrivacy();
            await context.SaveChangesAsync(Token);
        }
        await using var again = AnInstance.ContextFor(connection);
        Assert.Empty(await again.Bookmarks.ToListAsync(Token));
        Assert.True((await again.Bookmarks.IgnoreQueryFilters().SingleAsync(Token)).PrivateOrigin);
    }
}
