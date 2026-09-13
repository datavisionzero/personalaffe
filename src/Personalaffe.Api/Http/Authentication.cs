using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>
/// The door: a session cookie from a browser or
/// <c>Authorization: Bearer &lt;token&gt;</c> from everything else, in front of
/// the <c>/api</c> group and nowhere else (<c>docs/api.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// What comes through is a <see cref="Caller"/> on the request, which is what
/// <see cref="ICallerIdentity"/> answers to the acts. The principal carries the
/// id and nothing else: nothing downstream reads claims, because the caller is
/// a value the acts take whole rather than a bag of strings to parse back.
/// </para>
/// <para>
/// The five operations outside it — the version, the two health checks and the
/// two setup operations — say so with <c>AllowAnonymous</c>, one at a time,
/// where they are mapped. Everything else is behind it by default, which is the
/// way round that fails safe: a new endpoint that forgets to think about
/// authentication is closed, not open.
/// </para>
/// </remarks>
public static class Authentication
{
    public const string Scheme = "Personalaffe";

    /// <summary>
    /// What an endpoint the owner alone may reach asks for.
    /// </summary>
    /// <remarks>
    /// A policy rather than a line in each act, so that the refusal happens
    /// before the request body is even read: an agent asking for one of these
    /// should hear "not yours" rather than a remark about the shape of a field
    /// it was never going to be allowed to send. The acts check it again, which
    /// is not redundancy anybody should remove — an act is callable from more
    /// than one endpoint, and only one of them has this on it.
    /// </remarks>
    public const string OwnerPolicy = "owner";

    public static IServiceCollection AddPersonalaffeAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, DoorHandler>(
                Scheme, displayName: null, configureOptions: null);

        // Behind the door by default: the group asks for an authenticated
        // caller, and only what says AllowAnonymous is outside.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(Scheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(OwnerPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new OwnerRequirement()));

        services.AddSingleton<IAuthorizationHandler, OwnerHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICallerIdentity, CallerIdentity>();
        services.AddSingleton<LoginThrottle>();

        return services;
    }
}

/// <inheritdoc cref="Authentication"/>
public sealed class DoorHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggers,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var admit = Context.RequestServices.GetRequiredService<AuthenticateCaller>();

        Caller? caller;

        if (Request.Headers.Authorization.ToString() is { Length: > 0 } authorization)
        {
            caller = await admit.FromTokenAsync(authorization, Context.RequestAborted);
        }
        else if (Request.Cookies.TryGetValue(BrowserCookie.For(Request).Name, out var secret))
        {
            caller = await admit.FromSessionAsync(secret, Context.RequestAborted);
        }
        else
        {
            return AuthenticateResult.NoResult();
        }

        if (caller is null)
        {
            // Expired, revoked, never issued, or not a credential at all. Which
            // of them it was is not said and is not knowable from the answer.
            return AuthenticateResult.Fail("The presented credential admits nobody.");
        }

        Context.Features.Set(caller);

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, caller.Id.ToString())], Scheme.Name)),
            Scheme.Name));
    }

    /// <summary>
    /// The problem document rather than a bare status: a client branches on the
    /// code, and a 401 without a body would be the one refusal that has none.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Problems.WriteAsync(
            Context,
            RefusalCode.Unauthenticated,
            "Sign in with a browser, or send an agent token as `Authorization: Bearer <token>`.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Problems.WriteAsync(Context, RefusalCode.Forbidden, detail: null);
}

/// <summary>What <see cref="Authentication.OwnerPolicy"/> asks of the caller.</summary>
public sealed class OwnerRequirement : IAuthorizationRequirement;

/// <inheritdoc cref="OwnerRequirement"/>
public sealed class OwnerHandler(IHttpContextAccessor accessor) : AuthorizationHandler<OwnerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OwnerRequirement requirement)
    {
        if (accessor.HttpContext?.Features.Get<Caller>()?.IsOwner == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>The port answered from the request: whoever the door admitted.</summary>
public sealed class CallerIdentity(IHttpContextAccessor accessor) : ICallerIdentity
{
    public Caller Caller =>
        accessor.HttpContext?.Features.Get<Caller>()
        ?? throw new InvalidOperationException(
            "No authenticated caller on this request. Everything but the version, the two health checks "
            + "and the two setup operations is behind the door.");
}
