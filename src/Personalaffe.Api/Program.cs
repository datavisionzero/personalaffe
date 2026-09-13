// The composition root. Endpoints, authentication and the acts arrive with the
// code they belong to rather than as empty registrations placed here in
// advance (docs/codebase.md).

using Personalaffe.Api.Hosting;
using Personalaffe.Api.Http;
using Personalaffe.Application.Ports;
using Personalaffe.Infrastructure;
using Serilog;

// Serilog says what is wrong with Serilog here and nowhere else: a sink that
// cannot deliver writes to SelfLog and carries on, so a console that has gone
// away costs a line on standard error, never a request.
Serilog.Debugging.SelfLog.Enable(Console.Error);

// This binary serves the instance and has no verbs. A word handed to it is
// somebody looking for one — `personalaffe backup`, `personalaffe reset` — and
// the host would otherwise ignore it, start a second server beside the one
// already running and die on a port that is taken. What that person is looking
// for is `pea` or the database, so the answer says which, here, rather than
// twenty lines of stack trace later.
//
// A `--switch` is not a verb: that is the configuration the host itself reads,
// and it is left alone.
if (Array.Find(args, argument => !argument.StartsWith('-')) is { } verb)
{
    Console.Error.WriteLine($"""
        personalaffe: `{verb}` is not a command. This image serves the instance and takes no verbs.

        The workspace is reached with the CLI, over the API, from anywhere:
            pea version                     (docs/cli.md)
        The database is reached beside this container, not through it:
            docker compose exec db pg_dump -U personalaffe personalaffe > backup.sql
        """);
    return 2;
}

var builder = WebApplication.CreateBuilder(args);

// Everything read from the environment is read here, in one block, so that a
// value the instance will not accept stops the start with the one line that
// names the variable. Without it the exception escapes unhandled, and what the
// operator finds is a stack trace in a container restarting every few seconds.
// There is no logger yet — this runs before the host is built, which is why the
// message goes to stderr by hand.
try
{
    var logSettings = LogSettings.FromVariables(builder.Configuration[LogSettings.LevelVariable]);

    // The host arrives with a console provider of its own, and Serilog writes
    // to the console too: left in place, every line is logged twice. Clearing
    // them first and keeping `writeToProviders` is what leaves one console log
    // and still lets a provider somebody adds afterwards — a test listening for
    // what the instance complained about — see the same events.
    builder.Logging.ClearProviders();
    builder.Host.UseSerilog(
        (_, configuration) => LogSinks.Configure(configuration, logSettings),
        writeToProviders: true);

    // Read and validated here rather than inside the provider, so that "no
    // database configured" is a sentence naming the variable instead of an
    // InvalidOperationException out of the first request. The value itself
    // never reaches a log: DatabaseSettings.Redacted is what may be printed.
    builder.Services.AddPersonalaffeInfrastructure(DatabaseSettings.FromConnectionString(
        builder.Configuration.GetConnectionString(DatabaseSettings.ConnectionStringName)));
}
catch (ArgumentException refusal)
{
    Console.Error.WriteLine($"{refusal.Message} The instance will not start.");
    return 1;
}

// The clock is the base class library's, and the layers below know nothing
// about the container they are resolved from.
builder.Services.AddSingleton(TimeProvider.System);

// The schema, before anything is served, so that an installation is
// `docker compose up` and nothing else.
builder.Services.AddHostedService<SchemaMigrationService>();

var app = builder.Build();

// Method, path, status and duration — and nothing the owner or an agent wrote.
app.UseSerilogRequestLogging();

app.UseRouting();

// Everything the instance serves as an API is under one prefix, and everything
// else is the web application's (docs/codebase.md). An endpoint outside this
// group is a decision, not an oversight.
var api = app.MapGroup(Routes.Api);

api.MapHealth();

app.Run();

return 0;

/// <summary>
/// Named so that a test can start this instance in its own process. Top level
/// statements produce a class that is otherwise unreachable, and asking a
/// running instance what it does is the only way to say it.
/// </summary>
public partial class Program;
