using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Personalaffe.Application.Ports;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The whole instance in the test process, on a database of its own, with the
/// configuration an operator would have set.
/// </summary>
/// <remarks>
/// Every variable the instance reads is set here — to nothing where the test
/// wants none — so that a variable in the developer's real environment cannot
/// reach the test. Starting the host runs the migrations, which is why a test
/// about a refused start asks for a client and expects the throw.
/// </remarks>
internal sealed class AnInstance(
    string connectionString,
    IReadOnlyDictionary<string, string?>? configuration = null,
    Action<IServiceCollection>? registrations = null,
    string? environment = null) : WebApplicationFactory<Program>
{
    public static async Task<AnInstance> StartedAsync(PostgresFixture postgres) =>
        new(await postgres.CreateDatabaseAsync());

    /// <summary>The name the image's environment has, which is the default one.</summary>
    /// <remarks>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> starts everything as
    /// Development, and the framework decides some of what an instance does by
    /// that name. A test about the behaviour the image has rather than the
    /// behaviour a developer has asks for this one
    /// (<c>docs/operations.md</c>, "What the environment does not decide").
    /// </remarks>
    public const string Image = "Production";

    /// <summary>An instance started the way the image starts it.</summary>
    public static async Task<AnInstance> StartedAsAsync(PostgresFixture postgres, string environment) =>
        new(await postgres.CreateDatabaseAsync(), configuration: null, registrations: null, environment);

    /// <summary>
    /// An instance whose Trash is exactly the contributors given, and none of
    /// the product's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What these tests are about is the Trash itself — the fan-out, the
    /// permission filter, the guard on a restore, who may remove something for
    /// good — and a fake contributor is how that is said without dragging a
    /// content module in behind it.
    /// </para>
    /// <para>
    /// <strong>The product's own are removed rather than added to.</strong>
    /// Registrations are appended, and <c>Trash.Of</c> takes the first
    /// contributor for an application; leaving the real one in front of the
    /// fake would mean the test quietly asking a module it never set up. That
    /// is exactly what happened when Knowledge landed and the fake behind it
    /// stopped being reached. The real contributors have suites of their own.
    /// </para>
    /// </remarks>
    public static async Task<AnInstance> StartedWithTrashAsync(
        PostgresFixture postgres,
        params ITrash[] contributors) =>
        new(
            await postgres.CreateDatabaseAsync(),
            configuration: null,
            services => OnlyThisTrash(services, contributors));

    /// <inheritdoc cref="StartedWithTrashAsync(PostgresFixture, ITrash[])"/>
    public static async Task<AnInstance> StartedWithTrashAsync(
        PostgresFixture postgres,
        IReadOnlyDictionary<string, string?> configuration,
        params ITrash[] contributors) =>
        new(
            await postgres.CreateDatabaseAsync(),
            configuration,
            services => OnlyThisTrash(services, contributors));

    /// <summary>The same, against a database that already exists.</summary>
    public static AnInstance AgainstWithTrash(
        string connectionString,
        IReadOnlyDictionary<string, string?>? configuration,
        params ITrash[] contributors) =>
        new(connectionString, configuration, services => OnlyThisTrash(services, contributors));

    private static void OnlyThisTrash(IServiceCollection services, IReadOnlyList<ITrash> contributors)
    {
        services.RemoveAll<ITrash>();

        foreach (var contributor in contributors)
        {
            services.AddSingleton(contributor);
        }
    }

    /// <summary>
    /// An instance with something else registered as well. The registrations
    /// are appended to the host's, so what they add to an <c>IEnumerable</c>
    /// arrives beside whatever the product registered.
    /// </summary>
    public static async Task<AnInstance> StartedWithAsync(
        PostgresFixture postgres,
        Action<IServiceCollection> registrations,
        IReadOnlyDictionary<string, string?>? configuration = null) =>
        new(await postgres.CreateDatabaseAsync(), configuration, registrations);

    /// <summary>The same, against a database that already exists.</summary>
    public static AnInstance AgainstWith(
        string connectionString,
        Action<IServiceCollection> registrations,
        IReadOnlyDictionary<string, string?>? configuration = null) =>
        new(connectionString, configuration, registrations);

    public static AnInstance Against(string connectionString) => new(connectionString);

    /// <summary>An instance with something else in its environment as well.</summary>
    public static AnInstance Configured(string connectionString, IReadOnlyDictionary<string, string?> settings) =>
        new(connectionString, settings);

    /// <summary>The same database, started again with what the configuration says now.</summary>
    public AnInstance StartedAgain() => new(connectionString, configuration, registrations: null, environment);

    public string ConnectionString => connectionString;

    /// <summary>
    /// What the instance logged at warning or above — the reason behind a
    /// readiness answer, which the answer itself deliberately does not carry.
    /// </summary>
    public IReadOnlyList<string> Warnings =>
        [.. _logged.Where(line => line.Warning).Select(line => line.Text)];

    /// <summary>
    /// A place in the log to measure from, for a test whose subject is one
    /// request rather than the whole run.
    /// </summary>
    /// <remarks>
    /// <strong>Starting writes lines no request can be blamed for, and one of
    /// them depends on the machine.</strong> A checkout whose web application
    /// has not been built has no <c>src/Personalaffe.Api/wwwroot</c> — it is
    /// generated and git ignores it — and the host says so at warning level,
    /// twice. That is true of every fresh clone and of CI's integration job, and
    /// false on a laptop that has run the web build. So a test that asserts a
    /// request warned about nothing has to say which warnings it means, or it is
    /// a test that passes in one configuration and fails in the other
    /// (<c>docs/operations.md</c>, "What the environment does not decide").
    /// </remarks>
    public int Mark => _logged.Count;

    /// <summary>
    /// What was logged at warning or above after <paramref name="mark"/> was
    /// taken.
    /// </summary>
    public IReadOnlyList<string> WarningsSince(int mark) =>
        [.. _logged.Skip(mark).Where(line => line.Warning).Select(line => line.Text)];

    /// <summary>
    /// Everything the instance logged, at every level. What this is for is the
    /// one assertion that cannot be made from the outside: that no secret the
    /// owner or an agent holds is written down anywhere.
    /// </summary>
    public IReadOnlyList<string> Logged => [.. _logged.Select(line => line.Text)];

    private readonly List<(bool Warning, string Text)> _logged = [];

    public static PersonalaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<PersonalaffeDbContext>().UseNpgsql(connectionString).Options);

    public static SchemaMigrator MigratorFor(PersonalaffeDbContext context) =>
        new(context, NullLogger<SchemaMigrator>.Instance);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(settings =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
                ["PERSONALAFFE_LOG_LEVEL"] = string.Empty,
            };

            if (configuration is not null)
            {
                foreach (var pair in configuration)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            settings.AddInMemoryCollection(values);
        });

        // After the configuration above and before anything is built: the name
        // is host configuration like the connection string is, and the host
        // resolves it while it is reading that.
        if (environment is not null)
        {
            builder.UseEnvironment(environment);
        }

        if (registrations is not null)
        {
            builder.ConfigureServices(registrations);
        }

        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(new Capture(_logged));

            // Everything, so that the hygiene assertion has everything to look
            // through. The instance's own level is what an operator sets; this
            // is the test listening.
            logging.SetMinimumLevel(LogLevel.Trace);
        });

        return base.CreateHost(builder);
    }

    private sealed class Capture(List<(bool Warning, string Text)> logged) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Trace;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (logged)
            {
                logged.Add((logLevel >= LogLevel.Warning, $"{formatter(state, exception)}\n{exception}"));
            }
        }

        public void Dispose()
        {
        }
    }
}
