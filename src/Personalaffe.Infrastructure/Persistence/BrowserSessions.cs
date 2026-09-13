using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>The signed-in browsers (<see cref="IBrowserSessions"/>).</summary>
public sealed class BrowserSessions(PersonalaffeDbContext context) : IBrowserSessions
{
    public async Task AddAsync(BrowserSession session, CancellationToken cancellationToken)
    {
        context.BrowserSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<BrowserSession?> AdmitAsync(
        byte[] secretHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var session = await context.BrowserSessions
            .FirstOrDefaultAsync(candidate => candidate.SecretHash == secretHash, cancellationToken);

        if (session is null || !session.IsValid(now))
        {
            // Expired, revoked, never issued: one answer, and the caller cannot
            // tell which it was.
            return null;
        }

        // Being used is what keeps a session alive, and writing that down on
        // every request would make every read of the workspace a write.
        if (session.Touch(now))
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return session;
    }

    public async Task<IReadOnlyList<BrowserSession>> ListAsync(
        Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await context.BrowserSessions
            .Where(session => session.OwnerId == ownerId
                && session.RevokedAt == null
                && session.ExpiresAt > now
                && session.LastUsedAt > now - BrowserSession.IdleLifetime)
            .OrderByDescending(session => session.LastUsedAt)
            .ToListAsync(cancellationToken);

    public async Task RevokeAsync(
        Guid id, Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var session = await context.BrowserSessions.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.OwnerId == ownerId, cancellationToken);

        if (session is null)
        {
            // A session that is not this owner's is not a session this owner
            // can be told about.
            throw Refusal.NotFound("No such session.");
        }

        session.Revoke(now);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(
        Guid ownerId, Guid? except, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await context.BrowserSessions
            .Where(session => session.OwnerId == ownerId
                && session.RevokedAt == null
                && (except == null || session.Id != except))
            .ExecuteUpdateAsync(
                update => update.SetProperty(session => session.RevokedAt, now), cancellationToken);
    }
}
