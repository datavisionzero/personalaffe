using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Files;
using Personalaffe.Infrastructure;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.Api.Hosting;

/// <summary>
/// A backup, put back — which is the only thing that makes it a backup
/// (<c>docs/operations.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Everything slow and fallible happens before anything is
/// replaced.</strong> The archive is unpacked beside the storage root and every
/// part of it is weighed and hashed against the manifest; only then is the
/// database loaded, in one transaction, and only then do two renames put the
/// files in place. A restore that is going to fail fails while the instance is
/// still exactly as it was.
/// </para>
/// <para>
/// <strong>The manifest is what makes half a backup impossible to mistake for a
/// whole one.</strong> An archive without one is an interrupted backup, because
/// the manifest is written last. An archive whose manifest names a file it does
/// not carry is a dump without its volume. Both are refused by name rather than
/// restored into an instance that looks fine until somebody clicks download.
/// </para>
/// <para>
/// <strong>It will not write over a populated instance unless told to.</strong>
/// The accidental path — pointing a restore at the wrong instance — destroys
/// nothing and says what it found. The deliberate path is a flag long enough
/// that nobody types it by habit.
/// </para>
/// <para>
/// <strong>Every browser is signed out and every agent access survives.</strong>
/// A session in a dump is a browser that was signed in when the backup was
/// taken, possibly months ago and possibly signed out deliberately since;
/// bringing one back would be undoing a security decision the owner made. An
/// agent access is a credential the owner issued and has not revoked, and a
/// restore that silently broke every automation would be a restore nobody could
/// use. A revoked one stays revoked, because the dump says so.
/// </para>
/// </remarks>
public static class Restore
{
    /// <summary>The third word this image accepts.</summary>
    public const string Verb = "restore";

    /// <summary>Where the archive comes from: a file, or <c>-</c> for standard input.</summary>
    public const string FromFlag = "--from";

    /// <summary>What an operator says when they mean it.</summary>
    public const string AnywayFlag = "--over-a-populated-instance";

    /// <summary>
    /// Puts a backup back and answers the process's exit code: 0 when the
    /// instance is what the archive says, 1 when nothing was changed and the
    /// reason is on standard error, 2 when the arguments or the environment
    /// were wrong.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextWriter stderr,
        Func<Stream> standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(standardInput);

        if (!TryReadFlags(args, out var from, out var anyway, out var complaint))
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

        return await PutBackAsync(
            provider,
            storage,
            () => from == "-" ? standardInput() : File.OpenRead(from),
            anyway,
            stderr,
            TimeProvider.System,
            cancellationToken);
    }

    /// <summary>
    /// The restore itself, against services somebody else composed — which is
    /// what lets every refusal and the whole of the file half be tested without
    /// a PostgreSQL client on the machine running the tests.
    /// </summary>
    public static async Task<int> PutBackAsync(
        IServiceProvider provider,
        StorageSettings storage,
        Func<Stream> archive,
        bool anyway,
        TextWriter stderr,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(clock);

        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var root = storage.ResolvedRoot(Directory.GetCurrentDirectory());
        var files = Path.Combine(root, StorageAddress.Files);
        var unpacked = Path.Combine(root, $"restoring-{Guid.NewGuid():n}");
        var replaced = Path.Combine(root, $"replaced-{Guid.NewGuid():n}");

        try
        {
            if (!Directory.Exists(root))
            {
                await stderr.WriteLineAsync(
                    $"personalaffe: {root} is not there, so this is not an instance's storage root. "
                    + $"{StorageSettings.Variable} is what says where it is. Nothing was changed.");
                return 1;
            }

            var found = await WhatIsHereAsync(services, files, cancellationToken);

            if (found is not null && !anyway)
            {
                await stderr.WriteLineAsync(
                    $"personalaffe: this instance is not empty — {found}. A restore replaces all of it, "
                    + $"and nothing has been changed. Say {AnywayFlag} if that is what you mean.");
                return 1;
            }

            // Before anything is unpacked: a tool that is missing is a sentence
            // now rather than a storage root full of files and no database.
            var tool = await services.GetRequiredService<IDatabaseRestore>().ToolAsync(cancellationToken);

            Directory.CreateDirectory(unpacked);

            var description = await UnpackAsync(archive, unpacked, cancellationToken);

            Check(description, unpacked, services.GetRequiredService<SchemaMigrator>().Known);

            // The database first, because psql does it in one transaction and
            // is therefore the half that is all or nothing. The files second,
            // because two renames are as close to instant as this gets.
            await using (var dump = File.OpenRead(Path.Combine(unpacked, Backup.Dump)))
            {
                await services.GetRequiredService<IDatabaseRestore>().LoadAsync(dump, cancellationToken);
            }

            if (Directory.Exists(files))
            {
                Directory.Move(files, replaced);
            }

            var restored = Path.Combine(unpacked, StorageAddress.Files);

            if (Directory.Exists(restored))
            {
                Directory.Move(restored, files);
            }
            else
            {
                // A backup of an instance that had never stored a file.
                Directory.CreateDirectory(files);
            }

            await SignEveryBrowserOutAsync(services, clock, cancellationToken);
            await LetGoOfThePauseAsync(services, clock, cancellationToken);

            await stderr.WriteLineAsync($"""
                This instance is now what that backup says it is.

                  taken            {description.TakenAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z, by personalaffe {description.Version}
                  put back by      {tool}
                  the database     {description.Schema.Count} migration(s) of schema
                  the files        {description.Files.Count}, every one checked by its checksum
                  browsers         all signed out
                  agent access     as the backup had it, revocations included
                  writes           taken again: the pause in that dump was its own

                Start the instance. It applies any migrations this build has and that
                backup did not, which is how an upgrade by restore works.
                """);

            return 0;
        }
        catch (SchemaIsNewerException newer)
        {
            await stderr.WriteLineAsync($"personalaffe: {newer.Message} Nothing was changed.");
            return 1;
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            await stderr.WriteLineAsync($"personalaffe: {failed.Message}");
            return 1;
        }
        finally
        {
            Discard(unpacked);
            Discard(replaced);
        }
    }

    /// <summary>
    /// What is in this instance already, said the way an operator would
    /// recognise it — or nothing, which is what an empty instance answers.
    /// </summary>
    private static async Task<string?> WhatIsHereAsync(
        IServiceProvider services, string files, CancellationToken cancellationToken)
    {
        var stored = Directory.Exists(files)
            ? Directory.EnumerateFiles(files, "*", SearchOption.AllDirectories).Count()
            : 0;

        // Asked of the migrations history rather than of a table: a database
        // nothing has ever migrated has no tables to ask, and that is an empty
        // instance rather than an error.
        var migrated = await services.GetRequiredService<SchemaMigrator>().SchemaAsync(cancellationToken);

        var owned = migrated.Count > 0
                    && await services.GetRequiredService<IOwners>().ExistsAsync(cancellationToken);

        return (owned, stored) switch
        {
            (true, 0) => "it has an owner",
            (true, var many) => $"it has an owner and {many} stored file(s)",
            (false, 0) => null,
            (false, var many) => $"it has {many} stored file(s)",
        };
    }

    private static async Task<Description> UnpackAsync(
        Func<Stream> archive, string into, CancellationToken cancellationToken)
    {
        Description? description = null;

        await using (var source = archive())
        await using (var reading = new TarReader(source))
        {
            while (await reading.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                // Every name in an archive this product wrote is one it chose.
                // A name that climbs out of the directory is somebody else's
                // archive, or a tampered one, and either way it is not going to
                // be unpacked over the host.
                var where = Path.GetFullPath(Path.Combine(into, entry.Name));

                if (!where.StartsWith(into + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"`{entry.Name}` in that archive points outside it. This is not an archive "
                        + "personalaffe wrote. Nothing was changed.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(where)!);
                await entry.ExtractToFileAsync(where, overwrite: true, cancellationToken);

                if (entry.Name == Backup.Manifest)
                {
                    await using var manifest = File.OpenRead(where);

                    description = await JsonSerializer.DeserializeAsync<Description>(
                        manifest, ManifestFormat, cancellationToken);
                }
            }
        }

        return description ?? throw new InvalidOperationException(
            $"That archive has no {Backup.Manifest}, so it is not a backup this instance can put "
            + "back. A manifest is written last, which means an archive without one is a backup "
            + "that was interrupted. Nothing was changed.");
    }

    /// <summary>
    /// Every part the manifest names, weighed and hashed. This is the whole of
    /// how a damaged archive, a truncated one, and a dump that arrived without
    /// its volume are told from a good one.
    /// </summary>
    private static void Check(Description description, string unpacked, IReadOnlyList<string> known)
    {
        var newer = SchemaVersions.NotKnownHere(description.Schema, known);

        if (newer.Count > 0)
        {
            throw new SchemaIsNewerException(newer);
        }

        foreach (var part in description.Files.Prepend(description.Database))
        {
            var where = Path.Combine(unpacked, part.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(where))
            {
                throw new InvalidOperationException(
                    $"That archive's manifest names `{part.Path}` and the archive does not carry it. "
                    + "Half a backup is not a backup. Nothing was changed.");
            }

            var found = new FileInfo(where).Length;

            if (found != part.Bytes)
            {
                throw new InvalidOperationException(
                    $"`{part.Path}` is {found} bytes and its manifest says {part.Bytes}. That archive "
                    + "is damaged. Nothing was changed.");
            }

            using var reading = File.OpenRead(where);

            if (!string.Equals(
                    Convert.ToHexStringLower(SHA256.HashData(reading)), part.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"`{part.Path}` is the right length and not the right bytes. That archive is "
                    + "damaged. Nothing was changed.");
            }
        }
    }

    private static async Task SignEveryBrowserOutAsync(
        IServiceProvider services, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await services.GetRequiredService<IOwners>().FindAsync(cancellationToken) is not { } owner)
        {
            return;
        }

        await services.GetRequiredService<IBrowserSessions>()
            .RevokeAllAsync(owner.Id, except: null, clock.GetUtcNow(), cancellationToken);
    }

    /// <summary>
    /// The pause the backup was holding when it took the dump, let go of.
    /// </summary>
    /// <remarks>
    /// <strong>A backup contains its own maintenance pause.</strong> The dump is
    /// taken while the instance is held still, so the row saying so is in it —
    /// and restoring it hands a brand new instance a pause that a backup which
    /// finished minutes ago started. The instance would refuse every write until
    /// that original deadline passed, for a backup that is not happening, which
    /// is the sort of thing an owner meets once and never trusts a restore
    /// again. The restored instance is not being backed up; this says so.
    /// </remarks>
    private static async Task LetGoOfThePauseAsync(
        IServiceProvider services, TimeProvider clock, CancellationToken cancellationToken)
    {
        var maintenance = services.GetRequiredService<IMaintenance>();
        var pause = await maintenance.ReadAsync(cancellationToken);

        pause.End(clock.GetUtcNow());

        await maintenance.SaveAsync(cancellationToken);
    }

    private static void Discard(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // What is left is a directory under the storage root that this
            // product did not write, and the tidy-up leaves what it does not
            // recognise alone. An operator can remove it; a restore that failed
            // to tidy up is not a restore that failed.
        }
    }

    private static JsonSerializerOptions ManifestFormat { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static string Usage => $"""

        Usage: personalaffe {Verb} {FromFlag} FILE [{AnywayFlag}]
               personalaffe {Verb} {FromFlag} -    [{AnywayFlag}]

        It replaces this instance's database and its files with what the archive
        carries, and refuses to touch an instance that already has something in it
        unless the second flag says otherwise. The instance must not be serving:
        stop it first (docs/operations.md).
        """;

    private static bool TryReadFlags(string[] args, out string from, out bool anyway, out string complaint)
    {
        from = string.Empty;
        anyway = false;
        complaint = string.Empty;

        // args[0] is the verb itself.
        var rest = args[1..];

        anyway = Array.IndexOf(rest, AnywayFlag) >= 0;
        rest = [.. rest.Where(argument => argument != AnywayFlag)];

        if (rest.Length != 2 || rest[0] != FromFlag)
        {
            complaint = $"personalaffe: {Verb} takes {FromFlag}, and optionally {AnywayFlag}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rest[1]))
        {
            complaint = $"personalaffe: {FromFlag} needs a file, or - for standard input.";
            return false;
        }

        from = rest[1];

        if (from != "-" && !File.Exists(from))
        {
            complaint = $"personalaffe: {from} is not a file this instance can see.";
            return false;
        }

        return true;
    }

    /// <summary>One thing in the archive, and what it should still weigh.</summary>
    private sealed record Part(string Path, long Bytes, string Sha256);

    /// <summary>What the archive says it is — <see cref="Backup"/>'s manifest, read back.</summary>
    private sealed record Description(
        string Version,
        DateTimeOffset TakenAt,
        string DumpedBy,
        IReadOnlyList<string> Schema,
        Part Database,
        IReadOnlyList<Part> Files);
}
