using Microsoft.Extensions.Logging.Abstractions;
using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Infrastructure;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.Api.Hosting;

/// <summary>
/// The way back in when the password, the authenticator and the recovery codes
/// are all gone (<c>docs/operations.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// A verb on the binary that already has the connection string, and the one
/// verb this image has. It is not an endpoint and cannot become one: there is
/// no permission, no token and no header that reaches it. Its authorization is
/// that somebody is standing at the machine, which is the same authorization
/// <c>pg_dump</c> has — whoever has the host has the database.
/// </para>
/// <para>
/// personalaffe sends no mail, so there is no link to click and no address to
/// send one to. This is what stands where that would be, and it is written down
/// where an operator reads rather than left to be worked out on the day it is
/// needed.
/// </para>
/// <para>
/// The new password is read from a file or from standard input and never from
/// an argument: an argument stands in the shell history of the machine this is
/// run on, which is the one machine an owner who has just been locked out is
/// least able to clean up.
/// </para>
/// </remarks>
public static class OwnerRecovery
{
    /// <summary>The one word this image accepts.</summary>
    public const string Verb = "recover-owner";

    private const string PasswordFileFlag = "--password-file";

    /// <summary>
    /// Runs the recovery and answers the process's exit code: 0 when the owner
    /// can sign in again, 1 when nothing was changed and the reason is on
    /// standard error, 2 when the arguments were wrong.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!TryReadFlag(args, out var passwordFile, out var complaint))
        {
            await stderr.WriteLineAsync(complaint);
            await stderr.WriteLineAsync(Usage);
            return 2;
        }

        string password;
        try
        {
            password = (passwordFile == "-"
                ? await stdin.ReadToEndAsync(cancellationToken)
                : await File.ReadAllTextAsync(passwordFile, cancellationToken)).Trim('\r', '\n');
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync($"personalaffe: {passwordFile} could not be read: {unreadable.Message}");
            return 2;
        }

        var services = new ServiceCollection();

        try
        {
            services.AddPersonalaffeInfrastructure(DatabaseSettings.FromConnectionString(
                configuration.GetConnectionString(DatabaseSettings.ConnectionStringName)));
        }
        catch (ArgumentException refusal)
        {
            await stderr.WriteLineAsync($"personalaffe: {refusal.Message}");
            return 2;
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<SetTheOwnersPassword>();
        services.AddScoped<RecoverTheOwner>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        try
        {
            // A database this binary has not migrated is one whose owner row it
            // may not understand. Recovery is the wrong moment to find that out
            // by writing to it.
            if (!await scope.ServiceProvider.GetRequiredService<SchemaMigrator>()
                    .AppliedAsync(cancellationToken))
            {
                await stderr.WriteLineAsync(
                    "personalaffe: this database does not carry the schema this build knows. "
                    + "Start the instance so that it migrates, or start the build that wrote it. "
                    + "Nothing was changed.");
                return 1;
            }

            var recovered = await scope.ServiceProvider.GetRequiredService<RecoverTheOwner>()
                .ExecuteAsync(password, cancellationToken);

            await stdout.WriteLineAsync($"""
                The owner of this instance can sign in again.

                  email            {recovered.Email}
                  password         changed
                  second factor    {(recovered.SecondFactorWasOn ? "turned off, and the recovery codes with it" : "was already off")}
                  browsers         all signed out
                  agent access     untouched

                Sign in, and enrol an authenticator again if you want one.
                """);

            return 0;
        }
        catch (Refusal refusal)
        {
            await stderr.WriteLineAsync($"personalaffe: {refusal.Detail} Nothing was changed.");
            return 1;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            await stderr.WriteLineAsync($"personalaffe: the database could not be reached: {failure.Message}");
            return 1;
        }
    }

    private static string Usage => $"""

        Usage: personalaffe {Verb} {PasswordFileFlag} FILE
               personalaffe {Verb} {PasswordFileFlag} -      (the password on standard input)

        The new password is never an argument: it would stand in the shell history of
        the machine you are standing at. It is at least {Password.MinLength} characters.
        """;

    private static bool TryReadFlag(string[] args, out string passwordFile, out string complaint)
    {
        passwordFile = string.Empty;
        complaint = string.Empty;

        // args[0] is the verb itself.
        if (args.Length != 3 || args[1] != PasswordFileFlag)
        {
            complaint = $"personalaffe: {Verb} takes {PasswordFileFlag} and nothing else.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(args[2]))
        {
            complaint = $"personalaffe: {PasswordFileFlag} needs a file, or - for standard input.";
            return false;
        }

        passwordFile = args[2];
        return true;
    }
}
