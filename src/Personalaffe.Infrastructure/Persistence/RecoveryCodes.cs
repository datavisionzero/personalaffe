using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>The owner's recovery codes (<see cref="IRecoveryCodes"/>).</summary>
public sealed class RecoveryCodes(PersonalaffeDbContext context) : IRecoveryCodes
{
    public async Task ReplaceAsync(
        Guid ownerId, IReadOnlyList<RecoveryCode> codes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);

        // In one transaction, so that there is never a moment with no codes in
        // force and never a moment with two sets in force.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.RecoveryCodes
            .Where(code => code.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);

        context.RecoveryCodes.AddRange(codes);
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<int> RemainingAsync(Guid ownerId, CancellationToken cancellationToken) =>
        context.RecoveryCodes.CountAsync(
            code => code.OwnerId == ownerId && code.UsedAt == null, cancellationToken);

    public async Task<bool> ConsumeAsync(
        Guid ownerId, byte[] codeHash, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var code = await context.RecoveryCodes.FirstOrDefaultAsync(
            candidate => candidate.OwnerId == ownerId
                && candidate.CodeHash == codeHash
                && candidate.UsedAt == null,
            cancellationToken);

        if (code is null)
        {
            return false;
        }

        code.Use(at);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task ClearAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await context.RecoveryCodes
            .Where(code => code.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
}
