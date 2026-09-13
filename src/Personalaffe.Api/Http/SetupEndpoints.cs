using Personalaffe.Application.Acts;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>What <c>GET /api/setup</c> answers, and the whole of it.</summary>
public sealed record SetupStateResponse(bool Required);

/// <summary>What <c>POST /api/setup</c> takes.</summary>
public sealed record SetupRequest(string? Email, string? Password);

/// <summary>
/// The one-time setup (<c>docs/api.md</c>): an instance that belongs to nobody
/// acquires the one owner it will ever have.
/// </summary>
/// <remarks>
/// <para>
/// Both operations are outside the door, and they have to be: a browser at a
/// fresh installation cannot sign in, and something has to tell it so. They
/// stop being useful the moment an owner exists — the read says
/// <c>required: false</c> and the write is refused — and that is the whole of
/// the pre-authentication surface this epic adds.
/// </para>
/// <para>
/// Neither says who the owner is. An instance on the public internet answers
/// the read to whoever asks, so the answer is a boolean and never an address, a
/// name or a date.
/// </para>
/// </remarks>
public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetup(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/setup", async (ReadSetupState act, CancellationToken cancellationToken) =>
            {
                var state = await act.ExecuteAsync(cancellationToken);
                return Results.Ok(new SetupStateResponse(state.Required));
            })
            .AllowAnonymous()
            .WithName("ReadSetupState")
            .WithSummary("Whether this instance still needs its one-time setup.")
            .Produces<SetupStateResponse>();

        endpoints.MapPost("/setup", async (
                SetupRequest request,
                SetUpTheInstance act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(request.Email, request.Password, cancellationToken);

                // Nothing comes back. The owner has no representation a caller
                // is entitled to before it has signed in, and the credential
                // that setup would hand out is the session the sign-in makes.
                return Results.NoContent();
            })
            .AllowAnonymous()
            .WithName("SetUpTheInstance")
            .WithSummary("Claim an instance that has no owner. It works exactly once.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
