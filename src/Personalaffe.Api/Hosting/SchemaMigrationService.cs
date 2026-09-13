using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.Api.Hosting;

/// <summary>
/// Runs the migrations before the instance serves anything.
/// </summary>
/// <remarks>
/// A failed migration stops the instance: it does not start half-migrated and
/// it does not serve requests. Throwing out of <see cref="StartAsync"/> is
/// exactly that — the host does not come up, and the failure is in the log. A
/// schema newer than this code stops it the same way, for a different reason:
/// nothing was attempted, and nothing should be.
/// </remarks>
public sealed class SchemaMigrationService(
    IServiceScopeFactory scopeFactory,
    ILogger<SchemaMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<SchemaMigrator>();

        try
        {
            await migrator.ApplyAsync(cancellationToken);
        }
        catch (SchemaIsNewerException refusal)
        {
            // Not a failure — nothing was attempted. The operator has started an
            // old image against a database a later version already migrated, and
            // the sentence has to say which of the two moves rather than read
            // like a broken migration.
            logger.LogCritical("{Reason} The instance will not start.", refusal.Message);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Somebody stopped the container while it was starting. That is not
            // a fault to report as one; the lock has already been given back.
            logger.LogInformation("Start was cancelled before the schema was checked.");
            throw;
        }
        catch (Exception exception)
        {
            // The connection string is never in this line: what is logged is the
            // exception the provider raised, and the settings that could name a
            // database are redacted where they are printed (DatabaseSettings).
            logger.LogCritical(exception, "Migration failed; the instance will not start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
