using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// One application's Trash, stood in for.
/// </summary>
/// <remarks>
/// Scratchpad, Knowledge, Tasks and Files are PERSONAL-E5 to PERSONAL-E8 and
/// none of them exists, so this is what the surface fans out over. What is
/// under test is the surface — the permissions, the guard, the merge, the
/// owner-only removals — and not this; it does the least a contributor can do
/// and says so where it matters.
/// </remarks>
internal sealed class AFakeTrash(WorkspaceApplication application) : ITrash
{
    private readonly Dictionary<Guid, TrashEntry> _entries = [];

    public WorkspaceApplication Application => application;

    public List<(Guid Id, string? As)> Restored { get; } = [];

    public List<Guid> Removed { get; } = [];

    public int Emptied { get; private set; }

    public TrashEntry Holding(string name, DateTimeOffset deletedAt, Actor by, string? where = null)
    {
        var entry = new TrashEntry(
            application,
            Guid.CreateVersion7(deletedAt),
            name,
            where,
            deletedAt,
            by,
            deletedAt.AddDays(30),
            deletedAt);

        _entries[entry.Id] = entry;

        return entry;
    }

    public Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrashEntry>>(
            [.. _entries.Values.OrderByDescending(entry => entry.DeletedAt).Take(limit)]);

    public Task<RestoredTo?> RestoreAsync(
        Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return Task.FromResult<RestoredTo?>(null);
        }

        Guard(entry, held);
        _entries.Remove(id);
        Restored.Add((id, restoreAs));

        return Task.FromResult<RestoredTo?>(new RestoredTo(
            application, id, restoreAs ?? entry.Name, entry.Where, MovedToTheRoot: false));
    }

    public Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return Task.FromResult(false);
        }

        Guard(entry, held);
        _entries.Remove(id);
        Removed.Add(id);

        return Task.FromResult(true);
    }

    public List<DateTimeOffset> Purged { get; } = [];

    /// <summary>
    /// Completes the first time the instance sweeps, so that a test can wait
    /// for the background service rather than sleep and hope.
    /// </summary>
    public Task<DateTimeOffset> Swept => _swept.Task;

    private readonly TaskCompletionSource<DateTimeOffset> _swept =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// What every real contributor does: remove what was deleted on or before
    /// the moment it was handed, and nothing else. The retention itself never
    /// reaches here.
    /// </summary>
    public Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken)
    {
        Purged.Add(expiredBefore);
        _swept.TrySetResult(expiredBefore);

        var expired = _entries.Values
            .Where(entry => entry.DeletedAt <= expiredBefore)
            .Select(entry => entry.Id)
            .ToArray();

        foreach (var id in expired)
        {
            _entries.Remove(id);
        }

        return Task.FromResult(expired.Length);
    }

    public Task<int> EmptyAsync(CancellationToken cancellationToken)
    {
        Emptied += _entries.Count;
        var removed = _entries.Count;
        _entries.Clear();

        return Task.FromResult(removed);
    }

    /// <summary>
    /// What every real contributor will do with the version it was handed. It
    /// is here rather than in the act because an act that checked it would be
    /// checking a value it read a moment earlier, and the write that has to be
    /// atomic is the module's (<c>GuardedSave</c>).
    /// </summary>
    private static void Guard(TrashEntry entry, ContentVersion held)
    {
        var current = ContentVersion.Of(entry.UpdatedAt);

        if (!current.Matches(held))
        {
            throw Refusal.Stale($"{entry.Name} has changed since it was read.", current);
        }
    }
}
