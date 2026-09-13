using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.Api.Http;

/// <summary>
/// The two questions an orchestrator asks, and they are not the same question.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Liveness</strong> is whether this process is still a process worth
/// leaving alone. It touches nothing: a database that has gone away is a reason
/// to stop sending traffic, not a reason to kill and restart a healthy
/// container, and a liveness check that is a second database check turns every
/// outage into a restart loop.
/// </para>
/// <para>
/// <strong>Readiness</strong> is whether this instance can serve: the database
/// answers, and the schema it holds is the one this binary knows. It is asked
/// of the database every time rather than remembered from the start, because a
/// database that has gone away since is not ready whatever happened then.
/// </para>
/// <para>
/// Neither carries owner data, a connection string, or anything else a stranger
/// should not have. They answer a word and a status, and the reason a readiness
/// check failed is in the instance's log, where the operator is.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new HealthResponse("live")))
            .AllowAnonymous()
            .WithName("ReadLiveness")
            .WithSummary("Whether this process is running. It touches nothing else.")
            .Produces<HealthResponse>();

        endpoints.MapGet("/health/ready", async (
                SchemaMigrator migrator,
                ILoggerFactory loggers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return await migrator.AppliedAsync(cancellationToken)
                        ? Results.Ok(new HealthResponse("ready"))
                        : Results.Json(new HealthResponse("not-ready"), statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The caller gets a word; the operator gets the reason. The
                    // two are deliberately not the same answer: a readiness
                    // probe is reachable by anything that can reach the port.
                    loggers.CreateLogger(typeof(HealthEndpoints)).LogWarning(
                        exception, "Readiness could not be established.");

                    return Results.Json(
                        new HealthResponse("not-ready"), statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .AllowAnonymous()
            .WithName("ReadReadiness")
            .WithSummary("Whether the database answers and carries the schema this build knows.")
            // Both answers are the same shape and differ only in the word and
            // the status, and the document has to say so or a generated client
            // has nothing to read a failure with.
            .Produces<HealthResponse>()
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}

/// <summary>What the two health endpoints answer: a word, and nothing else.</summary>
public sealed record HealthResponse(string Status);
