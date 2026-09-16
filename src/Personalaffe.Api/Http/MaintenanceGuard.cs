using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>
/// What a write meets while a backup is taking both of this instance's stores
/// as of one moment (<see cref="MaintenancePause"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reads pass and writes wait.</strong> What a backup cannot survive is
/// the two stores moving apart while it has one of them; reading moves nothing.
/// So the workspace stays open, every page still opens and every file still
/// downloads, and what is refused is the small number of requests that would
/// change something — with a sentence saying what is happening and that it is
/// temporary, rather than a status a person has to look up.
/// </para>
/// <para>
/// <strong>After authorization, and not before.</strong> Two anonymous
/// operations write — claiming the instance, and signing in — and neither
/// touches the owner's content or a byte on the volume, so neither can put the
/// two stores out of step. Leaving them open means a backup does not lock the
/// owner out of their own workspace for the minute it runs, and it means an
/// unauthenticated caller is told nothing about what this instance is doing.
/// </para>
/// <para>
/// <strong>It reads the row every time.</strong> One single-row read against
/// the database the request was about to write to anyway, and no cache: a cache
/// whose answer is a moment old answers "not paused" during a backup, which is
/// the one wrong answer this exists to prevent.
/// </para>
/// </remarks>
public sealed class MaintenanceGuard(RequestDelegate next)
{
    private static readonly string[] Safe = ["GET", "HEAD", "OPTIONS"];

    /// <summary>What a refused caller is told to wait, at most.</summary>
    private static readonly TimeSpan WorthAskingAgain = TimeSpan.FromSeconds(5);

    public async Task InvokeAsync(HttpContext context, IMaintenance maintenance, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(maintenance);
        ArgumentNullException.ThrowIfNull(clock);

        var writing = !Safe.Contains(context.Request.Method, StringComparer.Ordinal)
                      && context.GetEndpoint() is { } endpoint
                      && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

        if (!writing)
        {
            await next(context);
            return;
        }

        var now = clock.GetUtcNow();
        var pause = await maintenance.ReadAsync(context.RequestAborted);

        if (!pause.Holds(now))
        {
            await next(context);
            return;
        }

        // How long before it is worth asking again, and **not** how long the
        // pause may last. Those are different numbers and only one of them is
        // any use to a client: the deadline is a ceiling of minutes that a
        // backup of a small workspace goes nowhere near, and a client told to
        // wait five minutes for half a second of stillness has been given a
        // worse answer than none. Asking again every few seconds costs a
        // refusal that is already cheap.
        var seconds = Math.Max(
            1,
            (int)Math.Ceiling(Math.Min(pause.Remaining(now).TotalSeconds, WorthAskingAgain.TotalSeconds)));

        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        await Problems.WriteAsync(
            context,
            RefusalCode.Paused,
            "This instance is being backed up, and writes are held for the moment so that its "
            + "database and its files are taken as of one moment. Try again in a few seconds; "
            + "nothing has been changed and nothing has been lost.");
    }
}
