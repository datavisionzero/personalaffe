// The composition root. Endpoints, authentication and the acts arrive with the
// code they belong to rather than as empty registrations placed here in
// advance (docs/codebase.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using Personalaffe.Api.Hosting;
using Personalaffe.Api.Http;
using Personalaffe.Application.Acts;
using Personalaffe.Application.Acts.Scratchpad;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Infrastructure;
using Serilog;

// Serilog says what is wrong with Serilog here and nowhere else: a sink that
// cannot deliver writes to SelfLog and carries on, so a console that has gone
// away costs a line on standard error, never a request.
Serilog.Debugging.SelfLog.Enable(Console.Error);

// This binary serves the instance and has exactly one verb. A word handed to it
// is somebody looking for one — `personalaffe backup`, `personalaffe reset` —
// and the host would otherwise ignore it, start a second server beside the one
// already running and die on a port that is taken. What that person is looking
// for is `pea`, the database, or the one verb below, so the answer says which,
// here, rather than twenty lines of stack trace later.
//
// A `--switch` is not a verb: that is the configuration the host itself reads,
// and it is left alone.
if (Array.Find(args, argument => !argument.StartsWith('-')) is { } verb)
{
    // The way back in when the password, the authenticator and the recovery
    // codes are all gone (docs/operations.md). It is here and not behind HTTP
    // because its authorization is that somebody is standing at the machine.
    if (verb == OwnerRecovery.Verb)
    {
        return await OwnerRecovery.RunAsync(
            args,
            // The environment, and nothing else: the connection string is
            // `ConnectionStrings__Postgres`, the same variable the instance
            // itself reads, so the verb needs no configuration of its own.
            new ConfigurationBuilder().AddEnvironmentVariables().Build(),
            Console.In,
            Console.Out,
            Console.Error);
    }

    Console.Error.WriteLine($"""
        personalaffe: `{verb}` is not a command. This image serves the instance and takes one verb.

        The way back in when the owner is locked out, on this machine (docs/operations.md):
            personalaffe {OwnerRecovery.Verb} --password-file -
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
TrustedProxies trustedProxies;
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

    // Where the owner's files will go (docs/operations.md). Nothing writes there
    // yet; what is read here is the path, and StorageService checks the place.
    builder.Services.AddSingleton(StorageSettings.FromVariables(
        builder.Configuration[StorageSettings.Variable]));

    // The two periods this instance keeps things for (docs/operations.md): how
    // long the Trash keeps what the owner deleted, and how long an unpinned
    // Scratchpad entry lasts. Two numbers, because they answer two questions.
    builder.Services.AddSingleton(RetentionSettings.FromVariables(
        builder.Configuration[RetentionSettings.Variable],
        builder.Configuration[RetentionSettings.ScratchpadVariable]));

    // Who may speak for the caller. Unset, nobody may, and the instance reads
    // the socket.
    trustedProxies = TrustedProxies.FromVariable(builder.Configuration[TrustedProxies.Variable]);
    builder.Services.AddSingleton(trustedProxies);

    // Where this instance is reached, when the operator has said. Optional, and
    // what it buys is a stricter check on browser writes (CsrfProtection).
    builder.Services.AddSingleton(PublicUrlSettings.FromVariables(
        builder.Configuration[PublicUrlSettings.Variable]));
}
catch (ArgumentException refusal)
{
    Console.Error.WriteLine($"{refusal.Message} The instance will not start.");
    return 1;
}

// The clock is the base class library's, and the layers below know nothing
// about the container they are resolved from.
builder.Services.AddSingleton(TimeProvider.System);

// One line per act, named, rather than an assembly scan: what a caller can do
// is a list somebody wrote, and an act that is not on it is not reachable by
// accident.
builder.Services.AddScoped<ReadSetupState>();
builder.Services.AddScoped<SetUpTheInstance>();
builder.Services.AddScoped<AuthenticateCaller>();
builder.Services.AddScoped<SignIn>();
builder.Services.AddScoped<SignOut>();
builder.Services.AddScoped<ReadMe>();
builder.Services.AddScoped<OwnerConfirmation>();
builder.Services.AddScoped<ReadSecurity>();
builder.Services.AddScoped<BeginSecondFactorEnrolment>();
builder.Services.AddScoped<ConfirmSecondFactorEnrolment>();
builder.Services.AddScoped<DisableSecondFactor>();
builder.Services.AddScoped<ReissueRecoveryCodes>();
builder.Services.AddScoped<SetTheOwnersPassword>();
builder.Services.AddScoped<ChangePassword>();
builder.Services.AddScoped<ListAgentAccess>();
builder.Services.AddScoped<GrantAgentAccess>();
builder.Services.AddScoped<ChangeAgentAccess>();
builder.Services.AddScoped<ReissueAgentToken>();
builder.Services.AddScoped<RevokeAgentAccess>();
builder.Services.AddScoped<ListSessions>();
builder.Services.AddScoped<RevokeSession>();
builder.Services.AddScoped<RevokeOtherSessions>();
builder.Services.AddScoped<ReadTheApplications>();
builder.Services.AddScoped<SwitchTheApplication>();
builder.Services.AddScoped<ReachingAnApplication>();
builder.Services.AddScoped<ReadTheTrash>();
builder.Services.AddScoped<RestoreFromTheTrash>();
builder.Services.AddScoped<RemoveFromTheTrash>();
builder.Services.AddScoped<EmptyTheTrash>();
builder.Services.AddScoped<PurgeTheTrash>();
builder.Services.AddScoped<ReadTheEntries>();
builder.Services.AddScoped<ReadAnEntry>();
builder.Services.AddScoped<CaptureAnEntry>();
builder.Services.AddScoped<RewriteAnEntry>();
builder.Services.AddScoped<DiscardAnEntry>();
builder.Services.AddScoped<ExpireTheEntries>();

// The door, in front of the `/api` group and nowhere else (docs/api.md).
builder.Services.AddPersonalaffeAuthentication();

// Order is start order, and both run before anything is served, so that an
// installation is `docker compose up` and nothing else. Storage first because it
// is the cheaper of the two to get wrong and the faster to answer.
builder.Services.AddHostedService<StorageService>();
builder.Services.AddHostedService<SchemaMigrationService>();

// After the migrator, because the first thing it does is read a table. It is a
// BackgroundService and the two before it are not, so it starts once they have
// finished rather than beside them.
builder.Services.AddHostedService<RetentionService>();

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

    // A field the object does not define is the caller's mistake and is said
    // out loud (docs/api.md, `unknown-field`). Ignoring it silently is how a
    // misspelled `passwrd` becomes a setup with an empty password and a person
    // who cannot tell why: an agent writing against this API has no screen to
    // notice on.
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

// Every refusal is one document (docs/api.md, Errors), and this is the one
// place that writes it.
builder.Services.AddExceptionHandler<Problems.Handler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

// Before anything reads a scheme or an address: the log line wants the caller's
// and so does the throttle on failed sign-ins, and the cookie's strictness
// follows the scheme. Only when an operator has named the proxy; an unnamed one
// is a client with a header.
if (trustedProxies.Configured)
{
    app.UseForwardedHeaders(trustedProxies.Options());
}

// Method, path, status and duration — and nothing the owner or an agent wrote.
app.UseSerilogRequestLogging();
app.UsePersonalaffeVersion();

app.UseRouting();

// After routing, because it asks the endpoint whether it is behind the door;
// before authorization, because what it establishes is who the caller is.
app.UseAuthentication();

// A write a browser makes proves it came from this application, and nothing a
// token holder does is affected (BrowserWriteGuard).
app.UseMiddleware<BrowserWriteGuard>();

app.UseAuthorization();

// Everything the instance serves as an API is under one prefix, and everything
// else is the web application's (docs/codebase.md). An endpoint outside this
// group is a decision, not an oversight.
var api = app.MapGroup(Routes.Api)
    // Behind the door by default, and outside it only where an endpoint says
    // AllowAnonymous. That is the way round that fails safe: an endpoint added
    // later without a thought about authentication is closed, not open.
    .RequireAuthorization()
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
api.MapSetup();
api.MapSession();
api.MapMe();
api.MapSecurity();
api.MapAgents();
api.MapApplications();
api.MapTrash();
api.MapScratchpad();

// An address under the prefix that no endpoint took is an API mistake and
// answers as one. Without this it would fall through to the web application's
// `index.html` once PERSONAL-4 puts that in front of it, and a client would
// have to tell a 200 of HTML from the JSON it asked for.
//
// Outside the door on purpose: what it says is which endpoints this build has,
// and the contract at /api/openapi/v1.json says that already, to anybody.
// Answering 401 here instead would mean a client could not tell "your token is
// wrong" from "this instance is older than you think".
api.MapFallback(context => Problems.WriteAsync(
    context,
    RefusalCode.NotFound,
    "This instance has no such endpoint. GET /api/openapi/v1.json is the contract it does have."))
    .AllowAnonymous();

// The web application: built by its own toolchain into wwwroot at image build
// time (deploy/Dockerfile) or by a local `npm run build`; in development the
// Vite dev server serves it and this finds nothing. Every path outside `/api`
// is the application's — its router decides what `/knowledge/architecture` is,
// and the instance never answers that address itself.
//
// After the group, and that order is the whole rule: an address under the
// prefix has already been answered by the API, by an endpoint or by the
// group's own not-found, so a client asking for an endpoint this instance does
// not have gets JSON rather than a 200 of HTML it has to recognise.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

return 0;

/// <summary>
/// Named so that a test can start this instance in its own process. Top level
/// statements produce a class that is otherwise unreachable, and asking a
/// running instance what it does is the only way to say it.
/// </summary>
public partial class Program;
