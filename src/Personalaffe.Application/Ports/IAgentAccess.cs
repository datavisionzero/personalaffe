using Personalaffe.Domain;

namespace Personalaffe.Application.Ports;

/// <summary>The named authorizations the owner has handed out.</summary>
public interface IAgentAccessStore
{
    /// <summary>All of them, revoked ones included, newest first.</summary>
    Task<IReadOnlyList<AgentAccess>> ListAsync(CancellationToken cancellationToken);

    Task<AgentAccess?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the name is already taken, case-insensitively, by something
    /// other than <paramref name="except"/>.
    /// </summary>
    Task<bool> NameIsTakenAsync(string name, Guid? except, CancellationToken cancellationToken);

    Task AddAsync(AgentAccess access, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The access a presented token admits, or nothing. Using it is what is
    /// recorded here; whether it still admits anybody is the caller's to check.
    /// </summary>
    Task<AgentAccess?> AdmitAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
}
