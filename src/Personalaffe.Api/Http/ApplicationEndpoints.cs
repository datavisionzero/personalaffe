using Personalaffe.Application.Acts;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>One application, its switch, and what the caller may do in it.</summary>
public sealed record ApplicationResponse(
    WorkspaceApplication Application,
    bool Enabled,
    Permission Permission,
    DateTimeOffset UpdatedAt)
{
    public static ApplicationResponse Of(TheApplication application) => new(
        application.Application, application.Enabled, application.Permission, application.UpdatedAt);
}

/// <summary>The four of them.</summary>
public sealed record ApplicationsResponse(IReadOnlyList<ApplicationResponse> Items);

/// <summary>What a switch is set to.</summary>
public sealed record SwitchApplicationRequest(bool Enabled);

/// <summary>
/// The application switch (<c>docs/api.md</c>, The applications): which of the
/// four focused areas this workspace has switched on.
/// </summary>
/// <remarks>
/// <para>
/// Reading is anybody the door admitted, because the set of four is in the
/// contract and the permission beside each is the caller's own. Switching is
/// the owner's alone, and guarded: two browsers deciding the shape of the
/// workspace at once is the case the guard exists for.
/// </para>
/// <para>
/// This is the one endpoint pair the switch has. What being switched off
/// <em>does</em> is not here — it is in <c>ReachingAnApplication</c>, which
/// every operation inside an application goes through, so that the answer
/// cannot differ between two of them.
/// </para>
/// </remarks>
public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplications(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/applications", async (
                ReadTheApplications act, CancellationToken cancellationToken) =>
            {
                var applications = await act.ExecuteAsync(cancellationToken);

                return Results.Ok(new ApplicationsResponse([.. applications.Select(ApplicationResponse.Of)]));
            })
            .WithName("ReadApplications")
            .WithSummary("The four applications, whether each is switched on, and what this caller may do in it.")
            .Produces<ApplicationsResponse>();

        endpoints.MapPut("/applications/{application}", async (
                string application,
                SwitchApplicationRequest body,
                HttpRequest request,
                SwitchTheApplication act,
                CancellationToken cancellationToken) =>
            {
                var switched = await act.ExecuteAsync(
                    Applications.Named(application),
                    body.Enabled,
                    EntityTags.Required(request),
                    cancellationToken);

                // The version the write produced, so that a client switching
                // twice in a row does not have to read in between.
                return Results.Ok(ApplicationResponse.Of(switched));
            })
            .WithName("SwitchApplication")
            .WithSummary("Switch one application on or off. The owner's alone.")
            .Produces<ApplicationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .NamesAnApplication()
            .Guarded();

        return endpoints;
    }
}
