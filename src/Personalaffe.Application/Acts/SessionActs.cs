using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>One signed-in browser, as the owner sees it in the list.</summary>
public sealed record SignedInBrowser(
    Guid Id,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool Current);

/// <summary>
/// Where this instance is signed in, so that the owner can end one they do not
/// recognise.
/// </summary>
public sealed class ListSessions(ICallerIdentity caller, IBrowserSessions sessions, TimeProvider clock)
{
    public async Task<IReadOnlyList<SignedInBrowser>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var who = caller.Caller.RequireOwner("see where this instance is signed in");

        var live = await sessions.ListAsync(who.Id, clock.GetUtcNow(), cancellationToken);

        return [.. live.Select(session => new SignedInBrowser(
            session.Id,
            session.Description,
            session.CreatedAt,
            session.LastUsedAt,
            session.ExpiresAt,
            session.Id == who.SessionId))];
    }
}

/// <summary>Ending one of them, which may be the one asking.</summary>
public sealed class RevokeSession(ICallerIdentity caller, IBrowserSessions sessions, TimeProvider clock)
{
    public async Task ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var who = caller.Caller.RequireOwner("end a signed-in browser");

        await sessions.RevokeAsync(id, who.Id, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>
/// Ending all of them but this one: what somebody does from the machine they
/// still have, about the one they no longer do.
/// </summary>
public sealed class RevokeOtherSessions(
    ICallerIdentity caller, IBrowserSessions sessions, TimeProvider clock)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var who = caller.Caller.RequireOwner("end the other signed-in browsers");

        await sessions.RevokeAllAsync(who.Id, who.SessionId, clock.GetUtcNow(), cancellationToken);
    }
}
