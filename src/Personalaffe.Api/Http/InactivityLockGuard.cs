using Microsoft.AspNetCore.Authorization;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>Marks the few operations a signed-in but locked browser may still reach.</summary>
public sealed class AllowWhileInactivityLocked;

/// <summary>
/// Enforces the browser lock on the server, after authentication and
/// authorization. Bearer-authenticated agents never carry browser-lock state
/// and therefore pass unchanged.
/// </summary>
public sealed class InactivityLockGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var caller = context.Features.Get<Caller>();
        var locked = caller is { SessionId: not null, InactivityLock.Locked: true };
        var endpoint = context.GetEndpoint();
        var allowed = endpoint?.Metadata.GetMetadata<AllowWhileInactivityLocked>() is not null
            || endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;

        if (locked && !allowed)
        {
            await Problems.WriteAsync(
                context,
                RefusalCode.Locked,
                "This browser session is locked after inactivity. Unlock it to continue.");
            return;
        }

        await next(context);
    }
}
