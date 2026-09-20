using Personalaffe.Application.Acts.Bookmarks;
// The composition root. Endpoints, authentication and the acts arrive with the
// code they belong to rather than as empty registrations placed here in
// advance (docs/codebase.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using Personalaffe.Api.Hosting;
using Personalaffe.Api.Http;
using Personalaffe.Application.Acts;
using Personalaffe.Application.Acts.Appearance;
using Personalaffe.Application.Acts.Dashboard;
using Personalaffe.Application.Acts.Files;
using Personalaffe.Application.Acts.Knowledge;
using Personalaffe.Application.Acts.Scratchpad;
using Personalaffe.Application.Acts.Search;
using Personalaffe.Application.Acts.Tasks;
using Personalaffe.Application.Acts.Weather;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Infrastructure;
using Serilog;

// Serilog says what is wrong with Serilog here and nowhere else: a sink that
// cannot deliver writes to SelfLog and carries on, so a console that has gone
// away costs a line on standard error, never a request.
Serilog.Debugging.SelfLog.Enable(Console.Error);

// This binary serves the instance and has exactly three verbs. A word handed to
// it is somebody looking for one — `personalaffe reset`, `personalaffe migrate` —
// and the host would otherwise ignore it, start a second server beside the one
// already running and die on a port that is taken. What that person is looking
// for is `pea`, the database, or one of the three verbs below, so the answer
// says which, here, rather than twenty lines of stack trace later.
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

    // One backup, both stores, and the pause that makes them agree
    // (docs/operations.md). It is here for the same reason recovery is: its
    // authorization is that somebody is standing at the machine, and it needs
    // the volume this container has mounted and the connection string it
    // already reads.
    if (verb == Backup.Verb)
    {
        return await Backup.RunAsync(
            args,
            new ConfigurationBuilder().AddEnvironmentVariables().Build(),
            Console.Error,
            Console.OpenStandardOutput);
    }

    // And the other direction, which is the only thing that makes a backup one.
    // It is not run against a serving instance: an operator stops the container
    // and runs this one-off beside it (docs/operations.md).
    if (verb == Restore.Verb)
    {
        return await Restore.RunAsync(
            args,
            new ConfigurationBuilder().AddEnvironmentVariables().Build(),
            Console.Error,
            Console.OpenStandardInput);
    }

    Console.Error.WriteLine($"""
        personalaffe: `{verb}` is not a command. This image serves the instance and takes three verbs.

        The way back in when the owner is locked out, on this machine (docs/operations.md):
            personalaffe {OwnerRecovery.Verb} --password-file -
        Both stores, taken as of one moment, with this instance held still (docs/operations.md):
            personalaffe {Backup.Verb} --to -  > personalaffe.tar
        And put back, with the instance stopped rather than held still:
            personalaffe {Restore.Verb} {Restore.FromFlag} - < personalaffe.tar
        The workspace is reached with the CLI, over the API, from anywhere:
            pea version                     (docs/cli.md)
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

    // Where the owner's files go and how much of it they may use
    // (docs/operations.md). The place is resolved once, here, so that the check
    // at startup and the store that writes into it cannot be two directories.
    var storage = StorageSettings.FromVariables(
        builder.Configuration[StorageSettings.Variable],
        builder.Configuration[StorageSettings.MaxFileVariable],
        builder.Configuration[StorageSettings.MaxTotalVariable]);

    builder.Services.AddSingleton(storage);
    builder.Services.AddSingleton(new StorageRoot(
        storage.ResolvedRoot(builder.Environment.ContentRootPath)));

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

    // Whether this instance asks anybody outside it about the weather, and how
    // often (docs/operations.md). Off is a real setting: an instance that is
    // not supposed to talk to anybody but its owner opens no socket at all.
    builder.Services.AddSingleton(WeatherSettings.FromVariables(
        builder.Configuration[WeatherSettings.Variable],
        builder.Configuration[WeatherSettings.FreshnessVariable]));

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
builder.Services.AddScoped<BookmarkActs>();
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
builder.Services.AddScoped<ReadTheFolder>();
builder.Services.AddScoped<ReadAFile>();
builder.Services.AddScoped<DownloadAFile>();
builder.Services.AddScoped<UploadAFile>();
builder.Services.AddScoped<ReplaceTheBytes>();
builder.Services.AddScoped<MakeAFolder>();
builder.Services.AddScoped<MoveOrRenameAFile>();
builder.Services.AddScoped<MoveOrRenameAFolder>();
builder.Services.AddScoped<DiscardAFile>();
builder.Services.AddScoped<DiscardAFolder>();
builder.Services.AddScoped<TidyTheStorage>();
builder.Services.AddScoped<ReadTheTree>();
builder.Services.AddScoped<ReadAPage>();
builder.Services.AddScoped<WriteANewPage>();
builder.Services.AddScoped<RewriteAPage>();
builder.Services.AddScoped<DiscardAPage>();
builder.Services.AddScoped<ReadTheHistory>();
builder.Services.AddScoped<ReadAnOldVersion>();
builder.Services.AddScoped<RecoverARevision>();
builder.Services.AddScoped<ExportTheKnowledge>();
builder.Services.AddScoped<ReadTheLists>();
builder.Services.AddScoped<MakeAList>();
builder.Services.AddScoped<RenameAList>();
builder.Services.AddScoped<DiscardAList>();
builder.Services.AddScoped<ReadTheTasks>();
builder.Services.AddScoped<CaptureATask>();
builder.Services.AddScoped<ReadATask>();
builder.Services.AddScoped<ChangeATask>();
builder.Services.AddScoped<DiscardATask>();
builder.Services.AddScoped<SearchTheWorkspace>();
builder.Services.AddScoped<ReadTheDashboard>();
builder.Services.AddScoped<ShowOrHideATile>();
builder.Services.AddScoped<ReadTheWeather>();
builder.Services.AddScoped<SetTheWeatherPlace>();
builder.Services.AddScoped<LookUpAPlace>();
builder.Services.AddScoped<ReadTheAppearance>();
builder.Services.AddScoped<SetTheAppearance>();

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

// And it is one document whatever ASPNETCORE_ENVIRONMENT says, which is the
// whole reason this line exists (docs/operations.md, "What the environment does
// not decide"). Left unset, the framework turns this on in Development and off
// everywhere else — and off means a body the reader cannot bind never reaches
// Problems.Handler at all: minimal APIs write an empty 400 themselves, so
// `unknown-field` and `validation` became a status with no document in the only
// configuration anybody installs. The suite could not see it, because a suite
// started by WebApplicationFactory runs in Development.
//
// A contract that depends on which environment an instance was started in is
// not a contract. TheEnvironmentDecidesNothingTests starts one as Production
// and asks it.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// The same reasoning, one layer down. The host validates the service graph and
// catches a scoped service captured by a singleton in Development only; pinned
// on, a graph this instance cannot build is a refused start rather than a
// request that fails in production and nowhere else. Both cost a fraction of
// one start-up and a pointer comparison per resolution.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

var app = builder.Build();

// Before anything reads a scheme or an address: the log line wants the caller's
// and so does the throttle on failed sign-ins, and the cookie's strictness
// follows the scheme. Only when an operator has named the proxy; an unnamed one
// is a client with a header.
if (trustedProxies.Configured)
{
    app.UseForwardedHeaders(trustedProxies.Options());
}

// Method, path, status, duration and who asked — and nothing the owner or an
// agent wrote.
//
// **Outside the exception handler, and that is the whole of its correctness.**
// Inside it, every Refusal an act throws passes through here on its way out,
// where it is an unhandled exception against a response that is still 500 — so
// an ordinary conflict was logged at error, as a 500 the caller was never told
// about, with a stack trace and with the title the owner typed in it. Two
// promises this file makes, broken by the order of two lines.
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms to {Caller}";

    // Read when the line is written, which is after the forwarded headers above
    // have had their say — so behind a named proxy this is the caller's address
    // and not the proxy's. An address is a fact about a connection; it is not
    // something the owner wrote, and it is the one thing an operator looking at
    // a run of refusals actually needs.
    options.EnrichDiagnosticContext = (diagnostic, http) => diagnostic.Set(
        "Caller", http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
});

// After the request logging, so that what it records is the status the caller
// was given rather than the exception on its way here.
app.UseExceptionHandler();

app.UsePersonalaffeVersion();

// And the headers that say what a browser may do with what it was given
// (SecurityHeaders). In front of everything for the same reason the version is:
// the answer that must not be framed, sniffed or allowed to call out is any of
// them, the refusals and the web application included.
app.UsePersonalaffeSecurityHeaders();

app.UseRouting();

// After routing, because it asks the endpoint whether it is behind the door;
// before authorization, because what it establishes is who the caller is.
app.UseAuthentication();

// A write a browser makes proves it came from this application, and nothing a
// token holder does is affected (BrowserWriteGuard).
app.UseMiddleware<BrowserWriteGuard>();

app.UseAuthorization();

// And a write meets the backup, if one is holding this instance still: reads
// pass, writes are told to come back in a moment (MaintenanceGuard). After
// authorization, so that what is refused is a caller who could otherwise have
// changed something.
app.UseMiddleware<MaintenanceGuard>();

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
api.MapFiles();
api.MapKnowledge();
api.MapTasks();
api.MapBookmarks();
api.MapSearch();
api.MapDashboard();
api.MapWeather();
api.MapAppearance();

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
