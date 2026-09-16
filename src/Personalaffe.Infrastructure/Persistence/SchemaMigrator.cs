using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations on startup, which is what lets an installation be
/// <c>docker compose up</c> and nothing else, and an upgrade a pull and an up
/// (<c>docs/codebase.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Migrations take a lock</strong>, so that two containers starting at
/// once do not migrate against each other — the second waits and then finds
/// nothing to do. A session-level advisory lock is the cheapest way to say that
/// in Postgres, because it needs no table that a migration might itself be
/// creating.
/// </para>
/// <para>
/// <strong>A newer schema than the code is refused</strong>, inside the same
/// lock, so that the comparison is not made against a database another
/// container is in the middle of migrating. A failed migration stops the
/// instance: that is the Api's <c>SchemaMigrationService</c>, which lets both
/// failures out.
/// </para>
/// <para>
/// <see cref="AppliedAsync"/> is the same question without the act, and it is
/// what readiness asks: the database answers, and the schema it holds is one
/// this binary wrote.
/// </para>
/// </remarks>
public sealed class SchemaMigrator(PersonalaffeDbContext context, ILogger<SchemaMigrator> logger)
{
    /// <summary>
    /// Any constant serves, as long as every personalaffe uses the same one.
    /// </summary>
    private const long AdvisoryLockKey = 0x_9E3_AFFE;

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "select pg_advisory_lock({0})", [AdvisoryLockKey], cancellationToken);

            // Asked first, and asked the other way round from "what is pending":
            // applied migrations the code does not know about. Asking only for
            // pending ones finds nothing on a database a later version has
            // migrated, and an old image would go on to serve requests against
            // a shape it misunderstands.
            var newer = SchemaVersions.NotKnownHere(
                await context.Database.GetAppliedMigrationsAsync(cancellationToken),
                context.Database.GetMigrations());

            if (newer.Count > 0)
            {
                throw new SchemaIsNewerException(newer);
            }

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

            if (pending.Length == 0)
            {
                logger.LogInformation("Schema is current; nothing to migrate.");
                return;
            }

            logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Length, pending);

            await context.Database.MigrateAsync(cancellationToken);

            logger.LogInformation("Schema is current.");
        }
        finally
        {
            // CancellationToken.None on purpose: a cancelled start still has to
            // give the lock back, and the connection this holds it on is about
            // to be closed.
            await context.Database.ExecuteSqlRawAsync(
                "select pg_advisory_unlock({0})", [AdvisoryLockKey], CancellationToken.None);
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Whether the database is reachable and carries exactly the schema this
    /// binary knows — nothing pending, and nothing it has never heard of.
    /// </summary>
    /// <remarks>
    /// It asks the database rather than remembering that the start went well,
    /// because readiness is about now: a database that has gone away since is
    /// not ready, whatever happened at startup.
    /// </remarks>
    public async Task<bool> AppliedAsync(CancellationToken cancellationToken)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        var known = context.Database.GetMigrations().ToArray();

        var unknown = SchemaVersions.NotKnownHere(applied, known);
        var pending = known.Except(applied, StringComparer.Ordinal).ToArray();

        return unknown.Count == 0 && pending.Length == 0;
    }

    /// <summary>
    /// The migrations this database carries, oldest first — which is what a
    /// schema is, said in the only way two builds can compare.
    /// </summary>
    public async Task<IReadOnlyList<string>> SchemaAsync(CancellationToken cancellationToken) =>
        [.. await context.Database.GetAppliedMigrationsAsync(cancellationToken)];
}
