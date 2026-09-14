using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Scratchpad;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The Scratchpad's entries in Postgres (<see cref="IScratchpadEntries"/>).
/// </summary>
/// <remarks>
/// The one store in this product whose <c>Remove</c> is a <c>DELETE</c>. Every
/// other module sets <c>deleted_at</c> and leaves the row where it is; a
/// Scratchpad entry is temporary by definition and is destroyed.
/// </remarks>
public sealed class ScratchpadEntries(PersonalaffeDbContext context) : IScratchpadEntries
{
    public async Task<IReadOnlyList<ScratchpadEntry>> ListAsync(
        int limit, CancellationToken cancellationToken) =>
        await context.ScratchpadEntries
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<ScratchpadEntry?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.ScratchpadEntries.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    public async Task AddAsync(ScratchpadEntry entry, CancellationToken cancellationToken)
    {
        await context.ScratchpadEntries.AddAsync(entry, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        GuardedSave.SaveAsync(context, "The entry", cancellationToken);

    public Task RemoveAsync(ScratchpadEntry entry, CancellationToken cancellationToken)
    {
        context.ScratchpadEntries.Remove(entry);

        // Guarded like any other write: the row carries updated_at as a
        // concurrency token, so a delete holding a version somebody has already
        // replaced removes nothing and is refused.
        return GuardedSave.SaveAsync(context, "The entry", cancellationToken);
    }

    public Task<int> ExpireAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken) =>
        context.ScratchpadEntries
            .Where(entry => !entry.Pinned && entry.UpdatedAt <= expiredBefore)
            // One statement, and no change tracking: the sweep is the instance
            // acting on a deadline and has nothing to say about the rows it
            // removes beyond how many there were.
            .ExecuteDeleteAsync(cancellationToken);
}
