using System.Diagnostics;
using System.Globalization;
using System.Text;
using Npgsql;
using Personalaffe.Application.Ports;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The database half of a backup, taken by the tool whose job that is
/// (<see cref="IDatabaseDump"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain SQL, no owners and no privileges.</strong> A restore is then
/// <c>psql</c> and nothing else — no second tool to have the right version of —
/// and it lands in whatever role the instance it is being restored into
/// happens to use, which is not necessarily the role it came out of. A human
/// can also read it, which for the one file standing between an owner and
/// everything they have written is worth more than a few megabytes.
/// </para>
/// <para>
/// <strong>The password is an environment variable and never an argument.</strong>
/// An argument stands in <c>ps</c> for every process on the machine for as long
/// as the dump takes.
/// </para>
/// <para>
/// <strong>An older tool is refused rather than tried.</strong> pg_dump will not
/// dump a server newer than itself and says so in a way nobody reads until the
/// day they need the backup. Asking both for their version first turns that into
/// one sentence before anything has been held still.
/// </para>
/// </remarks>
public sealed class PgDump(DatabaseSettings settings) : IDatabaseDump
{
    /// <summary>The tool, as it is found on the path.</summary>
    public const string Tool = "pg_dump";

    public async Task<string> ToolAsync(CancellationToken cancellationToken)
    {
        var version = await RunAsync([Tool, "--version"], cancellationToken);

        var server = await ServerVersionAsync(cancellationToken);
        var dumping = Major(version.Output);

        if (dumping is null)
        {
            throw new InvalidOperationException(
                $"{Tool} did not say which version it is: {version.Output.Trim()}");
        }

        if (dumping < server.Major)
        {
            throw new InvalidOperationException(
                $"{Tool} is version {dumping} and this database is PostgreSQL {server.Major}. "
                + "pg_dump will not dump a server newer than itself, so nothing has been held "
                + "still and no backup was started. Take the dump with the database's own "
                + $"{Tool} instead — it is in the container that runs it — or run a build of "
                + "this image that carries a newer one.");
        }

        return $"{version.Output.Trim()}, against PostgreSQL {server.Full}";
    }

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var connection = new NpgsqlConnectionStringBuilder(settings.ConnectionString);

        var start = Started(
        [
            Tool,
            "--host", connection.Host ?? "localhost",
            "--port", connection.Port.ToString(CultureInfo.InvariantCulture),
            "--username", connection.Username ?? string.Empty,
            "--dbname", connection.Database ?? string.Empty,
            "--no-owner",
            "--no-privileges",
            // What a restore into a database that is not empty needs, and
            // harmless into one that is: the restore refuses a populated
            // instance for its own reasons, and this is the second lock.
            "--clean",
            "--if-exists",
        ]);

        if (connection.Password is { Length: > 0 } password)
        {
            start.Environment["PGPASSWORD"] = password;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{Tool} could not be started.");

        var complaint = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.StandardOutput.BaseStream.CopyToAsync(destination, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{Tool} stopped with {process.ExitCode}: {(await complaint).Trim()}");
        }
    }

    /// <summary>
    /// The major version, out of a line like <c>pg_dump (PostgreSQL) 18.1</c>.
    /// </summary>
    private static int? Major(string version)
    {
        // Everything after the last bracket, which is where the number is, and
        // then the digits up to the first dot.
        var said = version.AsSpan(version.LastIndexOf(')') + 1).Trim();
        var digits = 0;

        while (digits < said.Length && char.IsAsciiDigit(said[digits]))
        {
            digits++;
        }

        return digits > 0 && int.TryParse(said[..digits], out var major) ? major : null;
    }

    private async Task<(int Major, string Full)> ServerVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var version = connection.PostgreSqlVersion;

        return (version.Major, version.ToString());
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string[] command, CancellationToken cancellationToken)
    {
        using var process = Process.Start(Started(command))
            ?? throw new InvalidOperationException($"{command[0]} could not be started.");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, output);
    }

    private static ProcessStartInfo Started(string[] command)
    {
        var start = new ProcessStartInfo(command[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in command[1..])
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }
}

/// <summary>
/// A dump, put back (<see cref="IDatabaseRestore"/>).
/// </summary>
/// <remarks>
/// <para>
/// <c>psql</c> and not <c>pg_restore</c>, because <see cref="PgDump"/> writes
/// plain SQL: one tool, present wherever the other one is, and a file a human
/// can read before they trust it.
/// </para>
/// <para>
/// <strong><c>ON_ERROR_STOP</c> is the whole of its safety.</strong> Without it
/// psql reports success having skipped every statement it could not run, and a
/// restore that half-happened is the one outcome nobody can recover from —
/// because it looks exactly like one that worked.
/// </para>
/// </remarks>
public sealed class Psql(DatabaseSettings settings) : IDatabaseRestore
{
    /// <summary>The tool, as it is found on the path.</summary>
    public const string Tool = "psql";

    public async Task<string> ToolAsync(CancellationToken cancellationToken)
    {
        var start = Started([Tool, "--version"]);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{Tool} could not be started.");

        var said = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return said.Trim();
    }

    public async Task LoadAsync(Stream dump, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dump);

        var connection = new NpgsqlConnectionStringBuilder(settings.ConnectionString);

        var start = Started(
        [
            Tool,
            "--host", connection.Host ?? "localhost",
            "--port", connection.Port.ToString(CultureInfo.InvariantCulture),
            "--username", connection.Username ?? string.Empty,
            "--dbname", connection.Database ?? string.Empty,
            // The first statement that fails stops the whole thing, inside one
            // transaction, so what is in the database afterwards is either the
            // dump or what was there before it.
            "--set", "ON_ERROR_STOP=on",
            "--single-transaction",
            "--quiet",
            "--no-psqlrc",
        ]);

        start.RedirectStandardInput = true;

        if (connection.Password is { Length: > 0 } password)
        {
            start.Environment["PGPASSWORD"] = password;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{Tool} could not be started.");

        var complaint = process.StandardError.ReadToEndAsync(cancellationToken);

        await dump.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
        process.StandardInput.Close();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{Tool} stopped with {process.ExitCode}: {(await complaint).Trim()}");
        }
    }

    private static ProcessStartInfo Started(string[] command)
    {
        var start = new ProcessStartInfo(command[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in command[1..])
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }
}
