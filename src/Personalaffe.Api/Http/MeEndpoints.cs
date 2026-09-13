using Personalaffe.Application.Acts;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>What <c>GET /api/me</c> answers.</summary>
/// <param name="Email">The owner's login identifier. An agent has none.</param>
/// <param name="Name">What the owner calls this agent access. The owner has none.</param>
public sealed record MeResponse(
    CallerKind Kind,
    string? Email,
    string? Name,
    PermissionsShape Permissions,
    DateTimeOffset Since);

/// <summary>
/// Who this credential admits (<c>docs/api.md</c>): the cheapest way for a
/// client to find out that it still works, which is what both of them do with
/// it.
/// </summary>
public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/me", async (ReadMe act, CancellationToken cancellationToken) =>
            {
                var who = await act.ExecuteAsync(cancellationToken);

                return Results.Ok(new MeResponse(
                    who.Kind, who.Email, who.Name, PermissionsShape.Of(who.Permissions), who.Since));
            })
            .WithName("ReadMe")
            .WithSummary("Who the presented credential admits.")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
