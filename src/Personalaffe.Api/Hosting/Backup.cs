using System.Formats.Tar;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Files;
using Personalaffe.Infrastructure;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.Api.Hosting;

/// <summary>
/// One backup, both stores, and the pause that makes them agree
/// (<c>docs/operations.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A file is a row in one store and bytes in the other.</strong> A
/// dump without the volume is a listing of files nobody can download; a volume
/// without the dump is bytes under names nobody can read. Taking them one after
/// the other with the instance running would be a backup with rows whose files
/// are missing, which is the one failure a restore cannot repair. So this holds
/// the instance still for the minute it takes, and the stillness has two halves.
/// </para>
/// <para>
/// <strong>The writers outside.</strong>
/// <see cref="Http.MaintenanceGuard"/> refuses writes while
/// <see cref="MaintenancePause"/> holds, with a sentence and a
/// <c>Retry-After</c>. Reads never stop: what the two stores could disagree
/// about is writing.
/// </para>
/// <para>
/// <strong>The writer inside.</strong> The hourly sweep destroys content
/// without being asked to, and a purge between the dump and the archive would
/// take away bytes the dump still has rows for. This takes the lock the purge
/// takes, and holds it for the whole run — so no sweep can start, and one that
/// had already started finishes before this begins. Nothing is timed and
/// nothing is hoped for.
/// </para>
/// <para>
/// <strong>The dump goes first and the files second</strong>, which is the same
/// order the instance itself writes in, upside down: bytes before rows means a
/// row implies its bytes existed earlier, so a dump taken before the archive
/// cannot name a file the archive does not carry. The pause is what closes the
/// other direction — something removing bytes in between — and the tidy-up is
/// deliberately not stopped, because all it removes is bytes no row points at.
/// </para>
/// <para>
/// <strong>What it says goes to standard error</strong>, all of it, so that
/// standard output is the backup and nothing else — whether or not that is
/// where it was sent.
/// </para>
/// </remarks>
public static class Backup
{
    /// <summary>The second word this image accepts.</summary>
    public const string Verb = "backup";

    /// <summary>Where the archive goes: a directory, or <c>-</c> for standard output.</summary>
    public const string ToFlag = "--to";

    /// <summary>The database, inside the archive.</summary>
    public const string Dump = "database.sql";

    /// <summary>What the archive is, inside the archive — and last, on purpose.</summary>
    public const string Manifest = "manifest.json";

    /// <summary>How long this waits for a sweep that is already running to finish.</summary>
    public static readonly TimeSpan WaitsForASweep = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Takes the backup and answers the process's exit code: 0 when the archive
    /// is complete, 1 when it is not and the reason is on standard error, 2 when
    /// the arguments or the environment were wrong.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter stderr,
        Func<Stream> standardOutput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(standardOutput);

        if (!TryReadFlag(args, out var to, out var complaint))
        {
            await stderr.WriteLineAsync(complaint);
            await stderr.WriteLineAsync(Usage);
            return 2;
        }

        var services = new ServiceCollection();
        StorageSettings storage;

        try
        {
            services.AddPersonalaffeInfrastructure(DatabaseSettings.FromConnectionString(
                configuration.GetConnectionString(DatabaseSettings.ConnectionStringName)));

            storage = StorageSettings.FromVariables(configuration[StorageSettings.Variable]);
        }
        catch (ArgumentException refusal)
        {
            await stderr.WriteLineAsync($"personalaffe: {refusal.Message}");
            return 2;
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        await using var provider = services.BuildServiceProvider();

        return await TakeAsync(
            provider, storage, to, stderr, standardOutput, TimeProvider.System, cancellationToken);
    }

    /// <summary>
    /// The backup itself, against services somebody else composed — which is
    /// what lets the whole of it be tested with a substituted dump, on a machine
    /// with no PostgreSQL client anywhere.
    /// </summary>
    public static async Task<int> TakeAsync(
        IServiceProvider provider,
        StorageSettings storage,
        string to,
        TextWriter stderr,
        Func<Stream> standardOutput,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(clock);

        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var root = storage.ResolvedRoot(Directory.GetCurrentDirectory());
        var files = Path.Combine(root, StorageAddress.Files);

        string tool;
        IReadOnlyList<string> schema;

        try
        {
            if (!await services.GetRequiredService<SchemaMigrator>().AppliedAsync(cancellationToken))
            {
                await stderr.WriteLineAsync(
                    "personalaffe: this database does not carry the schema this build knows. A backup "
                    + "taken now could not be described. Start the instance so that it migrates, or "
                    + "start the build that wrote it. Nothing was held still.");
                return 1;
            }

            schema = await services.GetRequiredService<SchemaMigrator>().SchemaAsync(cancellationToken);

            // Before anything is held still: a tool that is missing or too old
            // is a sentence now rather than a failed backup and a minute of
            // refused writes.
            tool = await services.GetRequiredService<IDatabaseDump>().ToolAsync(cancellationToken);

            // The root, and not `files/` under it: an instance that has never
            // stored a file has no such directory, and that is an instance with
            // no files rather than a reason not to back it up. A root that is
            // missing is a different matter — the instance itself refuses to
            // start without one.
            if (!Directory.Exists(root))
            {
                await stderr.WriteLineAsync(
                    $"personalaffe: {root} is not there, so this is not an instance's storage root. "
                    + $"{StorageSettings.Variable} is what says where it is. Nothing was held still.");
                return 1;
            }
        }
        catch (Exception unready) when (unready is not OperationCanceledException)
        {
            await stderr.WriteLineAsync($"personalaffe: {unready.Message}");
            return 1;
        }

        var startedAt = clock.GetUtcNow();

        try
        {
            await BeginAsync(services, clock, cancellationToken);
        }
        catch (Refusal refusal)
        {
            await stderr.WriteLineAsync($"personalaffe: {refusal.Detail}");
            return 1;
        }

        using var stillness = new CancellationTokenSource();
        var heartbeat = KeepStillAsync(provider, clock, stillness.Token);

        try
        {
            var written = false;

            // Waits for a sweep that is already running rather than refusing:
            // the pause is already on, so the sweep that is finishing is the
            // last one, and a backup that gave up because the clock struck the
            // hour would be a backup nobody could schedule.
            var giveUpAt = clock.GetUtcNow() + WaitsForASweep;

            do
            {
                written = await services.GetRequiredService<IExclusiveWork>().TryAsync(
                    PurgeTheTrash.Work,
                    async token => await WriteAsync(
                        services, files, to, tool, schema, startedAt, stderr, standardOutput, token),
                    cancellationToken);

                if (!written)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), clock, cancellationToken);
                }
            }
            while (!written && clock.GetUtcNow() < giveUpAt);

            if (!written)
            {
                await stderr.WriteLineAsync(
                    "personalaffe: something is sweeping the Trash and has not finished within "
                    + $"{WaitsForASweep.TotalSeconds:0} seconds. Nothing was written. This instance is "
                    + "writable again; try again in a moment.");
                return 1;
            }
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            await stderr.WriteLineAsync(
                $"personalaffe: the backup did not finish: {failed.Message} What was written is "
                + "incomplete — it has no manifest — and this instance is writable again.");
            return 1;
        }
        finally
        {
            await stillness.CancelAsync();
            await heartbeat;
            await EndAsync(provider, clock);
        }

        await stderr.WriteLineAsync(
            $"The instance was held still for {(clock.GetUtcNow() - startedAt).TotalSeconds:0.0} seconds.");

        return 0;
    }

    private static async Task WriteAsync(
        IServiceProvider services,
        string files,
        string to,
        string tool,
        IReadOnlyList<string> schema,
        DateTimeOffset startedAt,
        TextWriter stderr,
        Func<Stream> standardOutput,
        CancellationToken cancellationToken)
    {
        // The dump is spooled before it is archived, because a tar entry has to
        // say how long it is before it says what it is, and pg_dump does not
        // know how long its output is until it has finished writing it.
        var spooled = Path.Combine(Path.GetTempPath(), $"personalaffe-dump-{Guid.NewGuid():n}.sql");

        try
        {
            string database;

            await using (var dump = File.Create(spooled))
            {
                await services.GetRequiredService<IDatabaseDump>().WriteAsync(dump, cancellationToken);
            }

            database = await ChecksumAsync(spooled, cancellationToken);

            var where = to == "-"
                ? null
                : Path.Combine(to, $"personalaffe-{startedAt.UtcDateTime:yyyyMMdd-HHmmss}.tar");

            var destination = where is null ? standardOutput() : File.Create(where);
            IReadOnlyList<Part> stored;

            await using (destination)
            await using (var archive = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: true))
            {
                await archive.WriteEntryAsync(spooled, Dump, cancellationToken);

                stored = await ArchiveAsync(archive, files, cancellationToken);

                // Last, and that is the whole of how an interrupted backup is
                // told from a finished one: a tar that stops early has no
                // manifest, and a restore refuses it for exactly that reason.
                await WriteManifestAsync(
                    archive,
                    new Description(
                        InstanceVersion.Value,
                        startedAt,
                        tool,
                        schema,
                        new Part(Dump, new FileInfo(spooled).Length, database),
                        stored),
                    cancellationToken);
            }

            await stderr.WriteLineAsync(
                where is null
                    ? $"Wrote the backup to standard output: {stored.Count} file(s) beside the database."
                    : $"Wrote {where}: {stored.Count} file(s) beside the database.");
        }
        finally
        {
            File.Delete(spooled);
        }
    }

    private static async Task<IReadOnlyList<Part>> ArchiveAsync(
        TarWriter archive, string files, CancellationToken cancellationToken)
    {
        var stored = new List<Part>();

        if (!Directory.Exists(files))
        {
            // An instance that has never stored a file. Its backup carries the
            // database and nothing else, which is exactly what it has.
            return stored;
        }

        // Sorted, so that two backups of the same instance are comparable and a
        // manifest reads the same way twice. `incoming/` is not here: nothing
        // points at what is in it and the tidy-up empties it within the hour.
        foreach (var file in Directory
                     .EnumerateFiles(files, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var inside = $"{StorageAddress.Files}/{Path.GetRelativePath(files, file).Replace('\\', '/')}";

            stored.Add(new Part(inside, new FileInfo(file).Length, await ChecksumAsync(file, cancellationToken)));

            await archive.WriteEntryAsync(file, inside, cancellationToken);
        }

        return stored;
    }

    private static async Task WriteManifestAsync(
        TarWriter archive, Description description, CancellationToken cancellationToken)
    {
        var written = JsonSerializer.SerializeToUtf8Bytes(description, ManifestFormat);

        var entry = new PaxTarEntry(TarEntryType.RegularFile, Manifest)
        {
            DataStream = new MemoryStream(written),
        };

        await archive.WriteEntryAsync(entry, cancellationToken);
    }

    private static async Task<string> ChecksumAsync(string file, CancellationToken cancellationToken)
    {
        await using var reading = File.OpenRead(file);

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(reading, cancellationToken));
    }

    private static async Task BeginAsync(
        IServiceProvider services, TimeProvider clock, CancellationToken cancellationToken)
    {
        var maintenance = services.GetRequiredService<IMaintenance>();
        var pause = await maintenance.ReadAsync(cancellationToken);

        pause.Begin(clock.GetUtcNow(), MaintenancePause.Budget);

        await maintenance.SaveAsync(cancellationToken);
    }

    /// <summary>
    /// Pushes the deadline out while the work happens, so that the pause is
    /// always about to lapse and never does until this stops.
    /// </summary>
    private static async Task KeepStillAsync(
        IServiceProvider provider, TimeProvider clock, CancellationToken until)
    {
        var every = MaintenancePause.Budget / 3;

        try
        {
            while (!until.IsCancellationRequested)
            {
                await Task.Delay(every, clock, until);

                await using var scope = provider.CreateAsyncScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<IMaintenance>();
                var pause = await maintenance.ReadAsync(until);

                pause.Extend(clock.GetUtcNow(), MaintenancePause.Budget);

                await maintenance.SaveAsync(until);
            }
        }
        catch (OperationCanceledException)
        {
            // The work finished, which is the only way out of the loop.
        }
    }

    /// <summary>
    /// Lets go. On its own scope and with no cancellation token, because this
    /// runs in a <c>finally</c>: an instance left held still by a backup that
    /// was interrupted is an outage, and the deadline that would eventually end
    /// it is minutes away.
    /// </summary>
    private static async Task EndAsync(IServiceProvider provider, TimeProvider clock)
    {
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IMaintenance>();
            var pause = await maintenance.ReadAsync(CancellationToken.None);

            pause.End(clock.GetUtcNow());

            await maintenance.SaveAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Nothing to say and nowhere useful to say it: the pause carries its
            // own deadline for exactly this, and the instance is writable again
            // within minutes whatever happened here.
        }
    }

    private static JsonSerializerOptions ManifestFormat { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private static string Usage => $"""

        Usage: personalaffe {Verb} {ToFlag} DIRECTORY
               personalaffe {Verb} {ToFlag} -          (the archive on standard output)

        It holds this instance still for as long as it takes — reads keep working and
        writes are told to come back in a moment — and writes one tar carrying the
        database, the owner's files and a manifest of what is in it.
        """;

    private static bool TryReadFlag(string[] args, out string to, out string complaint)
    {
        to = string.Empty;
        complaint = string.Empty;

        // args[0] is the verb itself.
        if (args.Length != 3 || args[1] != ToFlag)
        {
            complaint = $"personalaffe: {Verb} takes {ToFlag} and nothing else.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(args[2]))
        {
            complaint = $"personalaffe: {ToFlag} needs a directory, or - for standard output.";
            return false;
        }

        to = args[2];

        if (to != "-" && !Directory.Exists(to))
        {
            complaint = $"personalaffe: {to} is not a directory this instance can see.";
            return false;
        }

        return true;
    }

    /// <summary>One thing in the archive, and what it should still weigh.</summary>
    private sealed record Part(string Path, long Bytes, string Sha256);

    /// <summary>
    /// What the archive is. Enough to tell two backups apart, to know which
    /// build and which schema wrote it, and to check every part of it without
    /// the instance it came from.
    /// </summary>
    private sealed record Description(
        string Version,
        DateTimeOffset TakenAt,
        string DumpedBy,
        IReadOnlyList<string> Schema,
        Part Database,
        IReadOnlyList<Part> Files);
}
