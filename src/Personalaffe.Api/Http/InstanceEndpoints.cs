using Personalaffe.Api.Hosting;

namespace Personalaffe.Api.Http;

/// <summary>What <c>GET /api/version</c> answers.</summary>
public sealed record VersionResponse(string Version);

/// <summary>
/// The instance itself (<c>docs/api.md</c>, Endpoints): the operation a client
/// calls before it knows whether its credential is any good.
/// </summary>
public static class InstanceEndpoints
{
    public static IEndpointRouteBuilder MapInstance(this IEndpointRouteBuilder endpoints)
    {
        // Outside the door by design, and it is the whole of what it says: a
        // version string. The CLI asks it before it knows whether its token
        // works, to report skew as skew and not as a refusal — and CI captures
        // the contract from an instance nobody has set up.
        endpoints.MapGet("/version", () => new VersionResponse(InstanceVersion.Value))
            .AllowAnonymous()
            .WithName("ReadVersion")
            .WithSummary("The version of this instance.");

        return endpoints;
    }
}
