using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// The sweep: what it asks each application for, what it does when somebody
/// else is already sweeping, and what one broken module costs the other three.
/// </summary>
public sealed class PurgeTheTrashTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_application_is_asked_for_everything_past_the_deadline()
    {
        var knowledge = new ACountingTrash(WorkspaceApplication.Knowledge, removes: 3);
        var files = new ACountingTrash(WorkspaceApplication.Files, removes: 1);

        var swept = await Sweeping([knowledge, files], days: 30).ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(swept.Swept);
        Assert.Equal(4, swept.Total);

        // The arithmetic happens once, here, so that four modules cannot come
        // to four different answers about when something expires.
        Assert.Equal(Now.AddDays(-30), knowledge.Asked);
        Assert.Equal(Now.AddDays(-30), files.Asked);
    }

    [Fact]
    public async Task A_changed_retention_changes_the_deadline_and_nothing_else()
    {
        var knowledge = new ACountingTrash(WorkspaceApplication.Knowledge, removes: 0);

        await Sweeping([knowledge], days: 7).ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now.AddDays(-7), knowledge.Asked);
    }

    [Fact]
    public async Task An_application_that_removed_nothing_is_not_in_the_answer()
    {
        var quiet = new ACountingTrash(WorkspaceApplication.Tasks, removes: 0);

        var swept = await Sweeping([quiet], days: 30).ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(swept.Swept);
        Assert.Empty(swept.Removed);
        Assert.Equal(0, swept.Total);
    }

    [Fact]
    public async Task When_another_instance_is_sweeping_this_one_does_nothing()
    {
        var knowledge = new ACountingTrash(WorkspaceApplication.Knowledge, removes: 3);
        var purge = new PurgeTheTrash(
            [knowledge], new SomebodyElseIsSweeping(), RetentionSettings.Default, new Fixed(Now));

        var swept = await purge.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.False(swept.Swept);
        Assert.Null(knowledge.Asked);
    }

    [Fact]
    public async Task One_broken_application_does_not_stop_the_others_from_ever_being_swept()
    {
        var broken = new ABrokenTrash(WorkspaceApplication.Files);
        var knowledge = new ACountingTrash(WorkspaceApplication.Knowledge, removes: 2);

        var swept = await Sweeping([broken, knowledge], days: 30)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(swept.Swept);
        Assert.Equal(2, swept.Total);
        Assert.Equal(WorkspaceApplication.Files, Assert.Single(swept.Failed).Application);
    }

    [Fact]
    public async Task An_instance_with_no_content_modules_sweeps_and_removes_nothing()
    {
        var swept = await Sweeping([], days: 30).ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(swept.Swept);
        Assert.Equal(0, swept.Total);
        Assert.Empty(swept.Failed);
    }

    private static PurgeTheTrash Sweeping(IEnumerable<ITrash> contributors, int days) =>
        new(contributors, new NobodyElse(), new RetentionSettings(TimeSpan.FromDays(days)), new Fixed(Now));

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NobodyElse : IExclusiveWork
    {
        public async Task<bool> TryAsync(
            string name, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
        {
            await work(cancellationToken);
            return true;
        }
    }

    private sealed class SomebodyElseIsSweeping : IExclusiveWork
    {
        public Task<bool> TryAsync(
            string name, Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class ACountingTrash(WorkspaceApplication application, int removes) : ITrash
    {
        public WorkspaceApplication Application => application;

        public DateTimeOffset? Asked { get; private set; }

        public Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken)
        {
            Asked = expiredBefore;
            return Task.FromResult(removes);
        }

        public Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrashEntry>>([]);

        public Task<bool> RestoreAsync(
            Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> EmptyAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class ABrokenTrash(WorkspaceApplication application) : ITrash
    {
        public WorkspaceApplication Application => application;

        public Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken) =>
            throw new IOException("The bytes are already gone.");

        public Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrashEntry>>([]);

        public Task<bool> RestoreAsync(
            Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> EmptyAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
