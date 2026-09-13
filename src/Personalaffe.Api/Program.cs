// The composition root. Endpoints, authentication and the acts arrive with the
// code they belong to rather than as empty registrations placed here in
// advance (docs/codebase.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using Personalaffe.Api.Hosting;
using Personalaffe.Api.Http;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
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

builder.Services.AddPersonalaffeOpenApi();

// JSON in, JSON out, snake_case fields (docs/api.md, Conventions). Enums travel
// as the names the contract spells, and integers are not accepted alongside, so
// a value that is not one of the set is refused at the door rather than stored
// as a row nobody can read.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(new Rfc3339());
});

// Every refusal is one document (docs/api.md, Errors), and this is the one
// place that writes it.
builder.Services.AddExceptionHandler<Problems.Handler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

// Method, path, status and duration — and nothing the owner or an agent wrote.
app.UseSerilogRequestLogging();
app.UsePersonalaffeVersion();

app.UseRouting();

// Everything the instance serves as an API is under one prefix, and everything
// else is the web application's (docs/codebase.md). An endpoint outside this
// group is a decision, not an oversight.
var api = app.MapGroup(Routes.Api)
    // The one answer every operation can give whatever else it does, said once
    // for the group rather than at each endpoint: a bug is a problem document
    // like any refusal (docs/api.md, Errors). It is also what puts the shape in
    // the contract, and what both generated clients read an error with.
    //
    // The refusals an operation can actually make are declared on that
    // operation, when it has one it can make. None of these three can.
    .ProducesProblem(StatusCodes.Status500InternalServerError);

// Outside the door: the contract is what a client compiles against before it
// has a credential, and what CI captures from an instance nobody has set up.
app.MapOpenApi($"{Routes.Api}/openapi/{{documentName}}.json");

api.MapInstance();
api.MapHealth();

// An address under the prefix that no endpoint took is an API mistake and
// answers as one. Without this it would fall through to the web application's
// `index.html` once PERSONAL-4 puts that in front of it, and a client would
// have to tell a 200 of HTML from the JSON it asked for.
api.MapFallback(context => Problems.WriteAsync(
    context,
    RefusalCode.NotFound,
    "This instance has no such endpoint. GET /api/openapi/v1.json is the contract it does have."));

app.Run();

return 0;

/// <summary>
/// Named so that a test can start this instance in its own process. Top level
/// statements produce a class that is otherwise unreachable, and asking a
/// running instance what it does is the only way to say it.
/// </summary>
public partial class Program;
