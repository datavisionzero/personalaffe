using Personalaffe.Api.Hosting;

namespace Personalaffe.Api.Http;

/// <summary>
/// <c>Personalaffe-Version</c> on every response, the refused and the failed
/// ones included: skew is reported by the CLI from whatever answer it got, and
/// a 401 is an answer.
/// </summary>
public static class VersionHeader
{
    /// <summary>The header every response carries.</summary>
    public const string Name = "Personalaffe-Version";

    public static IApplicationBuilder UsePersonalaffeVersion(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[Name] = InstanceVersion.Value;
                return Task.CompletedTask;
            });

            return next(context);
        });
}
