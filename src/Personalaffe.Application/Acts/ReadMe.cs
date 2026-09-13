using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>Who the credential admitted, as much of it as the caller is entitled to.</summary>
public sealed record Who(CallerKind Kind, string? Email, DateTimeOffset Since);

/// <summary>
/// The operation a client calls first: which kind of caller this credential
/// makes it, and what it is allowed to know about itself.
/// </summary>
/// <remarks>
/// It is the cheapest way for a client to find out that a credential still
/// works, which is what <c>pea</c> and the web application both do with it.
/// Agent access answers this too once it exists, with its name and its
/// permissions instead of an address (PERSONAL-12).
/// </remarks>
public sealed class ReadMe(ICallerIdentity caller, IOwners owners)
{
    public async Task<Who> ExecuteAsync(CancellationToken cancellationToken)
    {
        var who = caller.Caller;

        var owner = await owners.FindAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "A caller was admitted against an instance with no owner.");

        return new Who(who.Kind, owner.Email, owner.CreatedAt);
    }
}
