using Personalaffe.Application.Ports;
using Serilog;
using Serilog.Events;

namespace Personalaffe.Api.Hosting;

/// <summary>
/// One sink: the console, structured. A container's log is where an operator
/// looks first, and personalaffe must not need another running affe product to
/// have a log at all (<c>docs/codebase.md</c>).
/// </summary>
/// <remarks>
/// Nothing about a request body is logged. The request log carries method,
/// path, status and duration and nothing the owner or an agent wrote, because
/// this is a private workspace and its log is not a second copy of its
/// contents.
/// </remarks>
public static class LogSinks
{
    public static LoggerConfiguration Configure(LoggerConfiguration configuration, LogSettings settings)
    {
        var level = Enum.Parse<LogEventLevel>(settings.Level, ignoreCase: true);

        return configuration
            .MinimumLevel.Is(level)
            // The framework's own chatter stays out unless the floor is lowered
            // deliberately; the product's lines are the ones that matter. EF
            // Core is held down for a second reason: at Information it writes
            // every command it runs, parameters included.
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("application", "personalaffe")
            .Enrich.WithProperty("version", InstanceVersion.Value)
            .WriteTo.Console();
    }
}
