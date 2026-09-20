using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>
/// The cookie a browser session travels in, as the request's own scheme allows.
/// </summary>
/// <remarks>
/// <para>
/// Derived from the request rather than from the environment, because the two
/// disagree in the case that matters. An instance is real the moment it is
/// installed, and the first sign-in often happens over
/// <c>http://127.0.0.1:8080/</c>, before any proxy is in front of it. A
/// <c>__Host-</c> cookie is refused outright there and a <c>secure</c> one is
/// dropped, so the instance would answer <c>204</c>, the browser would keep
/// nothing, and the screen would come back to sign-in with nothing to say —
/// the one failure a person cannot debug from what they can see.
/// </para>
/// <para>
/// So: over HTTPS the strict cookie, with the prefix that binds it to this host
/// and this path; over plain HTTP a cookie without the prefix and without the
/// flag, which is the only kind that can work there. What that costs is said
/// where the choice is made — a session over plain HTTP travels in the clear,
/// and <c>docs/operations.md</c> says to put TLS in front of anything that is
/// not a trial.
/// </para>
/// <para>
/// The scheme is the caller's, which is why <see cref="TrustedProxies"/> stands
/// in front of this: behind a proxy that terminates TLS and is trusted to say
/// so, the request is HTTPS and the strict cookie is the one that is set.
/// </para>
/// </remarks>
public sealed record BrowserCookie(string Name, bool Secure)
{
    /// <summary>Over HTTPS. <c>__Host-</c> binds the cookie to this host and <c>/</c>.</summary>
    public const string SecureName = "__Host-personalaffe_session";

    /// <summary>Over plain HTTP, where a prefixed or <c>secure</c> cookie is not stored at all.</summary>
    public const string PlainName = "personalaffe_session";

    public static BrowserCookie For(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.IsHttps ? new BrowserCookie(SecureName, true) : new BrowserCookie(PlainName, false);
    }

    public CookieOptions Options(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = Secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
    };

    /// <summary>
    /// Takes both names out of the browser, not only the one this scheme would
    /// set. An instance that gained a TLS proxy after somebody signed in over
    /// plain HTTP still has the other cookie in that browser, and a sign-out
    /// that left it there would leave behind a secret the screen says is gone.
    /// </summary>
    public static void Forget(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        foreach (var name in new[] { SecureName, PlainName })
        {
            response.Cookies.Delete(
                name, new BrowserCookie(name, name == SecureName).Options(DateTimeOffset.UnixEpoch));
        }
    }
}

/// <summary>
/// A rolling window over failed sign-ins, keyed on the address that was tried
/// and on where it was tried from.
/// </summary>
/// <remarks>
/// <para>
/// One owner means one password, and one password means a guesser has exactly
/// one thing to guess. The account key is what makes that slow; the source key
/// is what stops one client from being handed the whole budget of a shared
/// address.
/// </para>
/// <para>
/// In memory, bounded, and deliberately not in the database: it protects a
/// sign-in, which is the operation that must not need a write, and an instance
/// that is restarted to clear it is an instance whose owner has the machine
/// anyway. The ceiling is what keeps a flood of made-up addresses from being a
/// way to fill the heap.
/// </para>
/// </remarks>
public sealed class LoginThrottle(TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private const int AccountLimit = 5;
    private const int AddressLimit = 20;
    private const int MaximumKeys = 4096;

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _attempts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public bool IsBlocked(string account, string source)
    {
        lock (_gate)
        {
            return Count("account:" + account) >= AccountLimit || Count("source:" + source) >= AddressLimit;
        }
    }

    public void Failed(string account, string source)
    {
        lock (_gate)
        {
            Add("account:" + account);
            Add("source:" + source);
            Trim();
        }
    }

    public void Succeeded(string account)
    {
        lock (_gate)
        {
            _attempts.TryRemove("account:" + account, out _);
        }
    }

    private int Count(string key)
    {
        if (!_attempts.TryGetValue(key, out var window))
        {
            return 0;
        }

        Prune(window);
        return window.Count;
    }

    private void Add(string key)
    {
        var window = _attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        Prune(window);
        window.Enqueue(clock.GetUtcNow());
    }

    private void Prune(Queue<DateTimeOffset> window)
    {
        var floor = clock.GetUtcNow() - Window;

        while (window.TryPeek(out var attempt) && attempt <= floor)
        {
            window.Dequeue();
        }
    }

    private void Trim()
    {
        if (_attempts.Count <= MaximumKeys)
        {
            return;
        }

        foreach (var pair in _attempts.Where(entry =>
        {
            Prune(entry.Value);
            return entry.Value.Count == 0;
        }).ToList())
        {
            _attempts.TryRemove(pair.Key, out _);
        }
    }
}

/// <summary>
/// A browser write proves itself twice: a header no cross-site form can set,
/// and an <c>Origin</c> that is this instance.
/// </summary>
/// <remarks>
/// With <c>PERSONALAFFE_PUBLIC_URL</c> set the origin is compared whole.
/// Without it the scheme is left out, because it is the one part the instance
/// cannot know: a reverse proxy that terminates TLS forwards the request as
/// <c>http</c> unless it is trusted to say otherwise
/// (<see cref="TrustedProxies"/>), and comparing that against the browser's
/// <c>https</c> would refuse every write an operator who had not set the
/// variable made. The host carries the check on its own — a foreign origin
/// cannot match it, and one that could would already be answering for this
/// instance.
/// </remarks>
public static class CsrfProtection
{
    /// <summary>What the web application sets on every write, and a form cannot.</summary>
    public const string Header = "X-Personalaffe-CSRF";

    public static bool IsSafe(HttpRequest request, Uri? publicUrl)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Headers[Header].ToString() != "1"
            || !Uri.TryCreate(request.Headers.Origin.ToString(), UriKind.Absolute, out var origin))
        {
            return false;
        }

        return publicUrl is null
            ? request.Host.HasValue
                && string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
            : Uri.Compare(
                publicUrl, origin, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0;
    }
}

/// <summary>
/// The guard in front of every write a browser makes, and nothing else.
/// </summary>
/// <remarks>
/// It applies exactly where the danger is: a request authenticated by a cookie,
/// which the browser attaches whoever caused the request to be made. A request
/// carrying a bearer token was not sent by a page on somebody else's site —
/// nothing attaches that header but the client that holds the token — so an
/// agent and <c>pea</c> never see this.
/// </remarks>
public sealed class BrowserWriteGuard(RequestDelegate next)
{
    private static readonly string[] Safe = ["GET", "HEAD", "OPTIONS"];

    public async Task InvokeAsync(HttpContext context, ICallerIdentity caller, PublicUrlSettings publicUrl)
    {
        ArgumentNullException.ThrowIfNull(context);

        var guarded = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
            && context.User.Identity?.IsAuthenticated == true
            && !Safe.Contains(context.Request.Method, StringComparer.Ordinal)
            && caller.Caller.SessionId is not null;

        if (guarded && !CsrfProtection.IsSafe(context.Request, publicUrl.Url))
        {
            await Problems.WriteAsync(
                context,
                RefusalCode.Forbidden,
                $"A write from a browser carries {CsrfProtection.Header}: 1 and an Origin of this instance. "
                + "This one carried neither, so it is not treated as coming from this application.");

            return;
        }

        await next(context);
    }
}

/// <summary>
/// The headers every answer carries, whatever asked for it and whatever it
/// answered.
/// </summary>
/// <remarks>
/// <para>
/// One policy, set in one place, in front of everything: the application, the
/// API, a download and every refusal. A header that is on some answers and not
/// others is one an attacker picks the answer that is missing it — and the
/// screen a browser is most likely to be tricked into rendering is the one an
/// endpoint wrote in a hurry.
/// </para>
/// <para>
/// <strong>What the policy has to allow is what this application actually
/// does.</strong> Its script is its own bundle and nothing else; its styles are
/// its own sheet <em>and</em> the stylesheets the Markdown editor writes into
/// the document at runtime, which is why <c>style-src</c> carries
/// <c>'unsafe-inline'</c> and <c>script-src</c> does not; its fonts are served
/// from this instance; its images are its own, the <c>data:</c> icon in
/// <c>index.html</c>, and whatever picture the owner put in a knowledge page,
/// which may be anywhere — so <c>img-src</c> admits <c>https:</c> and nothing
/// else. <c>connect-src 'self'</c> is the line that matters most: a page that
/// somehow ran somebody else's script still cannot send what it read anywhere.
/// </para>
/// <para>
/// <c>Strict-Transport-Security</c> follows the request's scheme for the same
/// reason the cookie does (<see cref="BrowserCookie"/>): sent over plain HTTP
/// it is ignored, and pinning a host that is still being reached at
/// <c>http://127.0.0.1:8080/</c> is how a first installation locks its own
/// owner out of it. It names this host only — an instance knows nothing about
/// the other names under the operator's domain and makes no promises for them.
/// </para>
/// </remarks>
public static class SecurityHeaders
{
    /// <summary>What a page may load, and where it may send what it has.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "base-uri 'self'; "
        + "object-src 'none'; "
        + "frame-ancestors 'none'; "
        + "form-action 'self'; "
        + "script-src 'self'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data: https:; "
        + "font-src 'self'; "
        + "connect-src 'self'";

    /// <summary>A year, this host, and no claim about any other.</summary>
    public const string StrictTransportSecurity = "max-age=31536000";

    /// <summary>
    /// The devices and the interfaces this application never asks for. Named
    /// rather than left to the default so that a dependency that starts asking
    /// is refused by the instance rather than by the owner noticing a prompt.
    /// </summary>
    public const string PermissionsPolicy =
        "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), "
        + "fullscreen=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), "
        + "midi=(), payment=(), usb=(), xr-spatial-tracking=()";

    public static IApplicationBuilder UsePersonalaffeSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                if (context.Request.Path.StartsWithSegments("/api"))
                    headers.CacheControl = "private, no-store";

                headers["Content-Security-Policy"] = ContentSecurityPolicy;

                // What the two say twice: `frame-ancestors` is the one modern
                // browsers read, and the older header is what a browser that
                // does not know it reads instead. Neither costs anything, and
                // this product is never in anybody's frame.
                headers["X-Frame-Options"] = "DENY";

                // Set here as well as on a download, because the answer that
                // must not be sniffed is any of them.
                headers["X-Content-Type-Options"] = "nosniff";

                // Nothing about a private workspace belongs in somebody else's
                // log — not the page a picture was linked from, and not the
                // address of this instance.
                headers["Referrer-Policy"] = "no-referrer";

                // A page this application opened cannot reach back into it.
                headers["Cross-Origin-Opener-Policy"] = "same-origin";

                headers["Permissions-Policy"] = PermissionsPolicy;

                if (context.Request.IsHttps)
                {
                    headers["Strict-Transport-Security"] = StrictTransportSecurity;
                }

                return Task.CompletedTask;
            });

            return next(context);
        });
    }
}
