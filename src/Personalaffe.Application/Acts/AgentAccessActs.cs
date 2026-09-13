using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>One agent access, as the owner sees it in the list.</summary>
public sealed record GrantedAccess(
    Guid Id,
    string Name,
    Permissions Permissions,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset TokenIssuedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>A newly granted access and the token, which is shown once.</summary>
public sealed record IssuedAccess(GrantedAccess Access, string Token);

/// <summary>
/// What the owner has let in.
/// </summary>
/// <remarks>
/// Revoked ones stay on the list. A revoked access still names the agent
/// everywhere it ever acted, and an owner asking "what did I hand out" wants
/// the whole answer, not the part that is still live.
/// </remarks>
public sealed class ListAgentAccess(ICallerIdentity caller, IAgentAccessStore store)
{
    public async Task<IReadOnlyList<GrantedAccess>> ExecuteAsync(CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("see what this instance has let in");

        var granted = await store.ListAsync(cancellationToken);

        return [.. granted.Select(Described)];
    }

    internal static GrantedAccess Described(AgentAccess access) => new(
        access.Id,
        access.Name,
        access.Permissions,
        access.TokenPrefix,
        access.CreatedAt,
        access.TokenIssuedAt,
        access.LastUsedAt,
        access.RevokedAt);
}

/// <summary>
/// Letting an agent in, with a name and one answer per application.
/// </summary>
/// <remarks>
/// The token comes back once and is afterwards nowhere: what the row keeps is a
/// digest and a recognisable head. An owner who loses it reissues rather than
/// recovers, which is the only honest thing a store of digests can offer.
/// </remarks>
public sealed class GrantAgentAccess(
    ICallerIdentity caller, IAgentAccessStore store, TimeProvider clock)
{
    /// <exception cref="Refusal">
    /// The caller is not the owner (<c>forbidden</c>), the name is missing or
    /// too long (<c>validation</c>), or it is already taken (<c>conflict</c>).
    /// </exception>
    public async Task<IssuedAccess> ExecuteAsync(
        string? name, Permissions? permissions, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("let an agent in");

        var normalized = AgentAccess.NormalizeName(name);

        if (await store.NameIsTakenAsync(normalized, except: null, cancellationToken))
        {
            throw Refusal.Conflict($"Something is already called {normalized}.");
        }

        var (access, token) = AgentAccess.Grant(
            normalized, permissions ?? Permissions.None, clock.GetUtcNow());

        await store.AddAsync(access, cancellationToken);

        return new IssuedAccess(ListAgentAccess.Described(access), token);
    }
}

/// <summary>Changing what an agent is called, or what it reaches.</summary>
public sealed class ChangeAgentAccess(
    ICallerIdentity caller, IAgentAccessStore store, TimeProvider clock)
{
    /// <exception cref="Refusal">
    /// The caller is not the owner, there is no such access, the name is taken,
    /// or the access has been revoked.
    /// </exception>
    public async Task<GrantedAccess> ExecuteAsync(
        Guid id, string? name, Permissions? permissions, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("change what an agent reaches");

        var access = await Found(store, id, cancellationToken);
        var now = clock.GetUtcNow();

        if (name is not null)
        {
            var normalized = AgentAccess.NormalizeName(name);

            if (await store.NameIsTakenAsync(normalized, access.Id, cancellationToken))
            {
                throw Refusal.Conflict($"Something is already called {normalized}.");
            }

            access.Rename(normalized, now);
        }

        if (permissions is not null)
        {
            access.Grant(permissions, now);
        }

        await store.SaveAsync(cancellationToken);

        return ListAgentAccess.Described(access);
    }

    internal static async Task<AgentAccess> Found(
        IAgentAccessStore store, Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken)
        ?? throw Refusal.NotFound("No such agent access.");
}

/// <summary>
/// A new token for an existing access, which is also how the old one stops
/// working.
/// </summary>
public sealed class ReissueAgentToken(
    ICallerIdentity caller, IAgentAccessStore store, TimeProvider clock)
{
    public async Task<IssuedAccess> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("issue a credential");

        var access = await ChangeAgentAccess.Found(store, id, cancellationToken);

        if (access.Revoked)
        {
            throw Refusal.Conflict(
                "That access is revoked. Revoking is not undone; let the agent in again under a new one.");
        }

        var token = access.ReissueToken(clock.GetUtcNow());
        await store.SaveAsync(cancellationToken);

        return new IssuedAccess(ListAgentAccess.Described(access), token);
    }
}

/// <summary>
/// Shutting an agent out, at once and for good.
/// </summary>
/// <remarks>
/// A timestamp and not a deletion: the row stays, so that a revoked access
/// still names the agent everywhere it ever acted. Revoking twice changes
/// nothing and is not an error — an owner who clicks it again wants it revoked,
/// and it is.
/// </remarks>
public sealed class RevokeAgentAccess(
    ICallerIdentity caller, IAgentAccessStore store, TimeProvider clock)
{
    public async Task<GrantedAccess> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("revoke a credential");

        var access = await ChangeAgentAccess.Found(store, id, cancellationToken);

        access.Revoke(clock.GetUtcNow());
        await store.SaveAsync(cancellationToken);

        return ListAgentAccess.Described(access);
    }
}
