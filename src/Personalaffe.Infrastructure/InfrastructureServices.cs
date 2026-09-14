using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Personalaffe.Application.Ports;
using Personalaffe.Infrastructure.Persistence;
using Personalaffe.Infrastructure.Security;

namespace Personalaffe.Infrastructure;

/// <summary>
/// What this layer offers the composition root.
/// </summary>
public static class InfrastructureServices
{
    /// <summary>
    /// Registers Postgres and the migrator. The settings are read and validated
    /// by the caller, so that a value the instance will not accept stops the
    /// start with the one line that names the variable rather than failing on
    /// the first request.
    /// </summary>
    public static IServiceCollection AddPersonalaffeInfrastructure(
        this IServiceCollection services,
        DatabaseSettings database)
    {
        services.AddSingleton(database);

        services.AddDbContext<PersonalaffeDbContext>(options => options
            .UseNpgsql(database.ConnectionString)
            // EF looks for the migrations history by querying it and letting the
            // query fail, so the first start of a fresh installation logs a
            // failed command at Error while everything is going exactly right.
            // The next person reading the log during a real outage should not
            // have to rule that out first. A command that failed and mattered is
            // reported by the exception it raised, where it is caught.
            .ConfigureWarnings(warnings => warnings.Log((RelationalEventId.CommandError, LogLevel.Debug))));
        services.AddScoped<SchemaMigrator>();

        // One instance at a time, for the work that must only happen once
        // however many containers are running.
        services.AddScoped<IExclusiveWork, ExclusiveWork>();

        // One store per port, beside the context that answers it.
        services.AddScoped<IOwners, Owners>();
        services.AddScoped<IBrowserSessions, BrowserSessions>();
        services.AddScoped<IRecoveryCodes, RecoveryCodes>();
        services.AddScoped<IAgentAccessStore, AgentAccessStore>();
        services.AddScoped<IApplicationSwitch, ApplicationSwitch>();
        services.AddScoped<IScratchpadEntries, ScratchpadEntries>();

        // Argon2id, and the only place that knows it is (docs/codebase.md).
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();

        return services;
    }
}
