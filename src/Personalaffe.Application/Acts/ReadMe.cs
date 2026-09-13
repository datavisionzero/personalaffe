using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>Who the credential admitted, as much of it as the caller is entitled to.</summary>
public sealed record Who(
    CallerKind Kind, string? Email, string? Name, Permissions Permissions, DateTimeOffset Since);

/// <summary>
/// The operation a client calls first: which kind of caller this credential
/// makes it, and what it is allowed to know about itself.
/// </summary>
/// <remarks>
/// It is the cheapest way for a client to find out that a credential still
/// works, which is what <c>pea</c> and the web application both do with it. An
/// agent is told its own name and exactly what it reaches — the owner's address
/// is not an agent's to know.
/// </remarks>
public sealed class ReadMe(ICallerIdentity caller, IOwners owners, IAgentAccessStore agents)
{
    public async Task<Who> ExecuteAsync(CancellationToken cancellationToken)
    {
        var who = caller.Caller;

        if (who.Kind == CallerKind.Agent)
        {
            var access = await agents.FindAsync(who.Id, cancellationToken)
                ?? throw new InvalidOperationException("A caller was admitted against an access that is gone.");

            return new Who(who.Kind, Email: null, access.Name, access.Permissions, access.CreatedAt);
        }

        var owner = await owners.FindAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "A caller was admitted against an instance with no owner.");

        return new Who(who.Kind, owner.Email, Name: null, Permissions.Full, owner.CreatedAt);
    }
}
