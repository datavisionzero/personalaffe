using Microsoft.EntityFrameworkCore;
using Npgsql;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The migration path against a real PostgreSQL: applied to an empty database,
/// found done on the next start, and refused when the database carries
/// something this binary has never heard of.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_empty_database_is_migrated_to_the_schema_this_binary_knows()
    {
        await using var context = AnInstance.ContextFor(await postgres.CreateDatabaseAsync());

        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));

        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            context.Database.GetMigrations(),
            await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_second_start_against_the_same_database_finds_nothing_to_do()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var first = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(first).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await using var second = AnInstance.ContextFor(connectionString);
        await AnInstance.MigratorFor(second).ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await second.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_scratchpad_table_carries_no_deleted_at()
    {
        // The one table in this schema with no way back out of a deletion
        // (`docs/api.md`, Deleting sets content aside). A `deleted_at` here
        // would be a column inviting somebody to wire the Scratchpad into the
        // Trash by habit, so its absence is asserted rather than remembered.
        await using var context = AnInstance.ContextFor(await postgres.CreateDatabaseAsync());
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        var columns = await ColumnsOfAsync(context, "scratchpad_entries");

        Assert.Equal(["created_at", "id", "pinned", "text", "updated_at"], columns);
    }

    [Fact]
    public async Task Two_starts_at_once_do_not_migrate_against_each_other()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var one = AnInstance.ContextFor(connectionString);
        await using var other = AnInstance.ContextFor(connectionString);

        await Task.WhenAll(
            AnInstance.MigratorFor(one).ApplyAsync(TestContext.Current.CancellationToken),
            AnInstance.MigratorFor(other).ApplyAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            one.Database.GetMigrations(),
            await one.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_schema_written_by_a_newer_binary_is_refused_rather_than_served()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var migrated = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(migrated).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await MigratedByANewerVersionAsync(connectionString, "29990101000000_SomethingThisBuildNeverHeardOf");

        await using var context = AnInstance.ContextFor(connectionString);

        var refusal = await Assert.ThrowsAsync<SchemaIsNewerException>(
            () => AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken));

        Assert.Contains("SomethingThisBuildNeverHeardOf", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("restore the backup", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_instance_refuses_to_start_against_a_schema_it_does_not_know()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var migrated = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(migrated).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await MigratedByANewerVersionAsync(connectionString, "29990101000000_SomethingThisBuildNeverHeardOf");

        await using var instance = AnInstance.Against(connectionString);

        await Assert.ThrowsAsync<SchemaIsNewerException>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_instance_started_against_a_database_that_is_not_there_says_so_and_does_not_serve()
    {
        await using var instance = AnInstance.Against(postgres.ConnectionStringFor("a_database_nobody_created"));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));

        Assert.IsType<PostgresException>(failure, exactMatch: false);
        Assert.Contains(
            "Migration failed; the instance will not start.",
            instance.Warnings.Select(warning => warning.Split('\n')[0]));
    }

    /// <summary>
    /// Writes a history row for a migration that does not exist in this build,
    /// which is what a database another version has migrated looks like from
    /// here.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ColumnsOfAsync(
        PersonalaffeDbContext context, string table)
    {
        var columns = new List<string>();

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            "select column_name from information_schema.columns where table_name = @table order by column_name",
            connection);
        command.Parameters.AddWithValue("table", table);

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task MigratedByANewerVersionAsync(string connectionString, string migration)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            values (@migration, '10.0.11')
            """,
            connection);
        command.Parameters.AddWithValue("migration", migration);
        await command.ExecuteNonQueryAsync();
    }
}
