using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>Serializes tree writes before reading ancestry, across all instances.</summary>
public sealed class BookmarkWork(PersonalaffeDbContext context) : IBookmarkWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Transaction-scoped: cancellation and rollback release this lock too.
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741903862)", cancellationToken);
        var result = await action(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
