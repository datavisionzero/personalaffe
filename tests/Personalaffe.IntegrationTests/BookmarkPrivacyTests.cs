using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BookmarkPrivacyTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_ancestor_hides_descendants_before_counting_paging_and_direct_reads_including_trash()
    {
        await using var db = AnInstance.ContextFor(await postgres.CreateDatabaseAsync());
        await AnInstance.MigratorFor(db).ApplyAsync(Token);
        var now = DateTimeOffset.UtcNow;
        var root = BookmarkFolder.Make("Secret ancestor", null, true, now);
        var child = BookmarkFolder.Make("Public flag does not override", root.Id, false, now);
        var hidden = Bookmark.Make("Secret first", "https://secret.example/", null, child.Id, now);
        var visible = Bookmark.Make("Visible", "https://example.com/", null, null, now.AddSeconds(1));
        db.BookmarkFolders.AddRange(root, child);
        db.Bookmarks.AddRange(hidden, visible);
        await db.SaveChangesAsync(Token);
        var owner = Caller.Owner(Guid.NewGuid());
        var query = BookmarkVisibility.Bookmarks(db, owner);
        Assert.Equal(1, await query.CountAsync(Token));
        Assert.Equal(visible.Id, (await query.OrderBy(link => link.CreatedAt).Take(1).SingleAsync(Token)).Id);
        Assert.Null(await query.SingleOrDefaultAsync(link => link.Id == hidden.Id, Token));
        Assert.Empty(await BookmarkVisibility.Folders(db, owner).ToListAsync(Token));
        Assert.Equal(2, await BookmarkVisibility.Bookmarks(db, owner with { PrivateBookmarks = true }).CountAsync(Token));

        root.DeletedAt = now; child.DeletedAt = now; hidden.DeletedAt = now;
        await db.SaveChangesAsync(Token);
        Assert.Null(await BookmarkVisibility.Bookmarks(db, owner, deleted: true).SingleOrDefaultAsync(link => link.Id == hidden.Id, Token));
        Assert.Equal(2, await BookmarkVisibility.Bookmarks(db, owner with { PrivateBookmarks = true }, deleted: true).CountAsync(Token));
    }

    [Fact]
    public async Task Privacy_changes_are_seen_by_another_context_without_stale_ancestry_caches()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = AnInstance.ContextFor(connection);
        await AnInstance.MigratorFor(db).ApplyAsync(Token);
        var now = DateTimeOffset.UtcNow;
        var folder = BookmarkFolder.Make("Folder", null, false, now);
        var child = BookmarkFolder.Make("Child", folder.Id, false, now);
        var link = Bookmark.Make("Link", "https://example.com/", null, child.Id, now);
        db.BookmarkFolders.AddRange(folder, child); db.Bookmarks.Add(link);
        await db.SaveChangesAsync(Token);
        await using var reader = AnInstance.ContextFor(connection);
        var owner = Caller.Owner(Guid.NewGuid());
        Assert.Equal(1, await BookmarkVisibility.Bookmarks(reader, owner).CountAsync(Token));
        folder.Change(folder.Name, null, true, now.AddSeconds(1));
        await db.SaveChangesAsync(Token);
        Assert.Equal(0, await BookmarkVisibility.Bookmarks(reader, owner).CountAsync(Token));
        child.Change(child.Name, null, false, now.AddSeconds(2));
        await db.SaveChangesAsync(Token);
        Assert.Equal(1, await BookmarkVisibility.Bookmarks(reader, owner).CountAsync(Token));
        link.PreservePrivacy();
        await db.SaveChangesAsync(Token);
        Assert.Equal(0, await BookmarkVisibility.Bookmarks(reader, owner).CountAsync(Token));
    }

    [Fact]
    public async Task Private_context_never_grants_application_permission()
    {
        await using var db = AnInstance.ContextFor(await postgres.CreateDatabaseAsync());
        var agent = Caller.Agent(AgentAccess.Grant("No access", Permissions.None, DateTimeOffset.UtcNow).Access)
            with { PrivateBookmarks = true };
        Assert.Throws<Refusal>(() => BookmarkVisibility.Bookmarks(db, agent));
        Assert.Throws<Refusal>(() => BookmarkVisibility.Folders(db, agent));
        var readOnly = agent with { Permissions = Permissions.None with { Bookmarks = Permission.Read } };
        Assert.Throws<Refusal>(() => readOnly.RequireWrite(WorkspaceApplication.Bookmarks));
    }

    [Fact]
    public async Task A_waiting_write_checks_visibility_after_the_privacy_change_commits()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var first = AnInstance.ContextFor(connection);
        await AnInstance.MigratorFor(first).ApplyAsync(Token);
        var now = DateTimeOffset.UtcNow;
        var folder = BookmarkFolder.Make("Folder", null, false, now);
        var link = Bookmark.Make("Link", "https://example.com/", null, folder.Id, now);
        first.BookmarkFolders.Add(folder); first.Bookmarks.Add(link);
        await first.SaveChangesAsync(Token);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var change = new BookmarkWork(first).ExecuteAsync(async token =>
        {
            folder.Change(folder.Name, null, true, now.AddSeconds(1));
            await first.SaveChangesAsync(token);
            changed.SetResult();
            await release.Task.WaitAsync(token);
            return true;
        }, Token);
        await changed.Task.WaitAsync(Token);
        await using var second = AnInstance.ContextFor(connection);
        var write = new BookmarkWork(second).ExecuteAsync(async token =>
            await BookmarkVisibility.Bookmarks(second, Caller.Owner(Guid.NewGuid()))
                .SingleOrDefaultAsync(row => row.Id == link.Id, token), Token);
        release.SetResult();
        await change;
        Assert.Null(await write);
    }

    [Fact]
    public async Task Private_header_is_request_local_and_api_responses_are_not_cached()
    {
        var seen = new List<bool>();
        await using var instance = new AnInstance(await postgres.CreateDatabaseAsync(), registrations: services =>
            services.AddScoped<ITrash>(provider => new ContextProbe(provider.GetRequiredService<ICallerIdentity>(), seen)));
        using var owner = await AnOwner.SignedInAsync(instance, Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/trash");
        request.Headers.Add("Personalaffe-Private", "true");
        using var response = await owner.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        using var ordinary = await owner.GetAsync("/api/trash", Token);
        ordinary.EnsureSuccessStatusCode();
        Assert.True(ordinary.Headers.CacheControl?.NoStore);
        using var malformed = new HttpRequestMessage(HttpMethod.Get, "/api/trash");
        malformed.Headers.Add("Personalaffe-Private", "false");
        using var refusedContext = await owner.SendAsync(malformed, Token);
        refusedContext.EnsureSuccessStatusCode();
        Assert.Equal([true, false, false], seen);
    }

    private sealed class ContextProbe(ICallerIdentity caller, List<bool> seen) : ITrash
    {
        public WorkspaceApplication Application => WorkspaceApplication.Bookmarks;
        public Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken)
        {
            seen.Add(caller.Caller.PrivateBookmarks);
            return Task.FromResult<IReadOnlyList<TrashEntry>>([]);
        }
        public Task<RestoredTo?> RestoreAsync(Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> EmptyAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
