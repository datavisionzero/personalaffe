using Microsoft.EntityFrameworkCore;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>The agents the owner has let in (<see cref="IAgentAccessStore"/>).</summary>
public sealed class AgentAccessStore(PersonalaffeDbContext context) : IAgentAccessStore
{
    public async Task<IReadOnlyList<AgentAccess>> ListAsync(CancellationToken cancellationToken) =>
        await context.AgentAccess
            .OrderByDescending(access => access.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<AgentAccess?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.AgentAccess.FirstOrDefaultAsync(access => access.Id == id, cancellationToken);

    public Task<bool> NameIsTakenAsync(string name, Guid? except, CancellationToken cancellationToken) =>
        context.AgentAccess.AnyAsync(
            access => access.Name.ToLower() == name.ToLower() && (except == null || access.Id != except),
            cancellationToken);

    public async Task AddAsync(AgentAccess access, CancellationToken cancellationToken)
    {
        context.AgentAccess.Add(access);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public async Task<AgentAccess?> AdmitAsync(
        byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var access = await context.AgentAccess
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);

        if (access is null)
        {
            return null;
        }

        // Kept roughly: writing it on every request would make every read an
        // agent makes a write as well, to keep something measured in minutes up
        // to date.
        if (access.Used(now))
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return access;
    }
}
