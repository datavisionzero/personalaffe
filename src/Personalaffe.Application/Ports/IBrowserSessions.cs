using Personalaffe.Domain;

namespace Personalaffe.Application.Ports;

/// <summary>The signed-in browsers, as the acts and the door need them.</summary>
public interface IBrowserSessions
{
    Task AddAsync(BrowserSession session, CancellationToken cancellationToken);

    /// <summary>
    /// The session behind a presented secret, or nothing — expired, revoked and
    /// never-issued are all nothing, because the answer must not say which.
    /// Using a session is what keeps it alive, and this is where that is
    /// recorded.
    /// </summary>
    Task<BrowserSession?> AdmitAsync(byte[] secretHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The owner's sessions that still admit anybody, newest first.</summary>
    Task<IReadOnlyList<BrowserSession>> ListAsync(
        Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Revokes one session of the owner's. A session that is not theirs is not found.</summary>
    Task RevokeAsync(Guid id, Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every session of the owner's except <paramref name="except"/>,
    /// which is how a password change ends the ones somebody else is holding.
    /// </summary>
    Task RevokeAllAsync(Guid ownerId, Guid? except, DateTimeOffset now, CancellationToken cancellationToken);
}
