using Personalaffe.Application.Acts;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>What a caller may do, one answer per application.</summary>
public sealed record PermissionsShape(
    Permission Scratchpad, Permission Knowledge, Permission Tasks, Permission Files, Permission Bookmarks = Permission.None)
{
    public static PermissionsShape Of(Permissions permissions) => new(
        permissions.Scratchpad, permissions.Knowledge, permissions.Tasks, permissions.Files, permissions.Bookmarks);

    public Permissions Granted() => new(Scratchpad, Knowledge, Tasks, Files, Bookmarks);
}

/// <summary>One agent access, as the owner sees it.</summary>
public sealed record AgentResponse(
    Guid Id,
    string Name,
    PermissionsShape Permissions,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset TokenIssuedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt)
{
    public static AgentResponse Of(GrantedAccess access) => new(
        access.Id,
        access.Name,
        PermissionsShape.Of(access.Permissions),
        access.TokenPrefix,
        access.CreatedAt,
        access.TokenIssuedAt,
        access.LastUsedAt,
        access.RevokedAt);
}

/// <summary>An access and the token that reaches it, which is shown once.</summary>
public sealed record AgentTokenResponse(AgentResponse Agent, string Token);

/// <summary>Letting an agent in.</summary>
public sealed record GrantAgentRequest(string? Name, PermissionsShape? Permissions);

/// <summary>Changing one. What is not sent is not changed.</summary>
public sealed record ChangeAgentRequest(string? Name, PermissionsShape? Permissions);

/// <summary>
/// Agent access (<c>docs/api.md</c>): named, revocable, and the owner's to hand
/// out.
/// </summary>
/// <remarks>
/// Every operation here is the owner's alone, and an agent is refused whatever
/// its permissions say — not by a permission that could be granted, but because
/// no permission for this exists. An agent that could issue a credential could
/// issue itself a better one.
/// </remarks>
public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgents(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/agents", async (ListAgentAccess act, CancellationToken cancellationToken) =>
            {
                var granted = await act.ExecuteAsync(cancellationToken);

                return Results.Ok(granted.Select(AgentResponse.Of));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ListAgents")
            .WithSummary("What this instance has let in, revoked ones included.")
            .Produces<IReadOnlyList<AgentResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPost("/agents", async (
                GrantAgentRequest request, GrantAgentAccess act, CancellationToken cancellationToken) =>
            {
                var issued = await act.ExecuteAsync(
                    request.Name, request.Permissions?.Granted(), cancellationToken);

                // The token is in this answer and in no other, ever.
                return Results.Created(
                    $"{Routes.Api}/agents/{issued.Access.Id}",
                    new AgentTokenResponse(AgentResponse.Of(issued.Access), issued.Token));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("GrantAgentAccess")
            .WithSummary("Let an agent in. The token comes back once and is afterwards nowhere.")
            .Produces<AgentTokenResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPatch("/agents/{id:guid}", async (
                Guid id,
                ChangeAgentRequest request,
                ChangeAgentAccess act,
                CancellationToken cancellationToken) =>
            {
                var changed = await act.ExecuteAsync(
                    id, request.Name, request.Permissions?.Granted(), cancellationToken);

                return Results.Ok(AgentResponse.Of(changed));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ChangeAgentAccess")
            .WithSummary("Rename an agent, or change what it reaches. What is not sent is not changed.")
            .Produces<AgentResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/agents/{id:guid}/token", async (
                Guid id, ReissueAgentToken act, CancellationToken cancellationToken) =>
            {
                var issued = await act.ExecuteAsync(id, cancellationToken);

                return Results.Ok(new AgentTokenResponse(AgentResponse.Of(issued.Access), issued.Token));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("ReissueAgentToken")
            .WithSummary("A new token for an agent. The old one stops working.")
            .Produces<AgentTokenResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapDelete("/agents/{id:guid}", async (
                Guid id, RevokeAgentAccess act, CancellationToken cancellationToken) =>
            {
                var revoked = await act.ExecuteAsync(id, cancellationToken);

                // The row stays and the list keeps it: a revoked access still
                // names the agent everywhere it ever acted.
                return Results.Ok(AgentResponse.Of(revoked));
            })
            .RequireAuthorization(Authentication.OwnerPolicy)
            .WithName("RevokeAgentAccess")
            .WithSummary("Shut an agent out, at once and for good.")
            .Produces<AgentResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
