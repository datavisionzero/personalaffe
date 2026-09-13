using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    IReadOnlyDictionary<string, string?>? configuration = null) : WebApplicationFactory<Program>
{
    public static async Task<AnInstance> StartedAsync(PostgresFixture postgres) =>
        new(await postgres.CreateDatabaseAsync());

    public static AnInstance Against(string connectionString) => new(connectionString);

    /// <summary>An instance with something else in its environment as well.</summary>
    public static AnInstance Configured(string connectionString, IReadOnlyDictionary<string, string?> settings) =>
        new(connectionString, settings);

    /// <summary>The same database, started again with what the configuration says now.</summary>
    public AnInstance StartedAgain() => new(connectionString, configuration);

    public string ConnectionString => connectionString;

    /// <summary>
    /// What the instance logged at warning or above — the reason behind a
    /// readiness answer, which the answer itself deliberately does not carry.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    private readonly List<string> _warnings = [];

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

        builder.ConfigureLogging(logging => logging.AddProvider(new Capture(_warnings)));

        return base.CreateHost(builder);
    }

    private sealed class Capture(List<string> warnings) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                lock (warnings)
                {
                    warnings.Add($"{formatter(state, exception)}\n{exception}");
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
