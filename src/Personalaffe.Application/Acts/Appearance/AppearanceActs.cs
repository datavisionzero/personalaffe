using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Appearance;

namespace Personalaffe.Application.Acts.Appearance;

/// <summary>
/// What this instance is called and what its mark looks like.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is read by anybody who can reach the port.</strong> That is the
/// price of the two places it is worth having — the sign-in screen and the
/// browser tab, both of which are drawn before anybody has signed in — and it
/// is a price the owner is told about on the screen where they choose the name.
/// What is given away is a word they wrote and one of seven colours. Who the
/// owner is stays behind the door.
/// </para>
/// <para>
/// No caller is asked for. An act that took an identity it does not use would
/// be an act somebody later adds a check to by accident.
/// </para>
/// </remarks>
public sealed class ReadTheAppearance(IInstanceAppearance appearances)
{
    public Task<InstanceAppearance> ExecuteAsync(CancellationToken cancellationToken) =>
        appearances.ReadAsync(cancellationToken);
}

/// <summary>
/// Names this instance and chooses its mark. The owner's alone.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The owner's, because the answer is public.</strong> Everything else
/// an agent may write is content behind the door; this is the one setting whose
/// value whoever can reach the port can read. An agent that could write it
/// could write a word of its choosing onto the sign-in screen of somebody
/// else's instance, which is not what "read and write the Scratchpad" was
/// granted for. It is the same short list as issuing a credential, switching an
/// application and saying where the weather is for.
/// </para>
/// <para>
/// An agent still reads it: <see cref="ReadTheAppearance"/> asks nobody who
/// they are, so a console that names the instance in its own output needs no
/// credential for it and no permission.
/// </para>
/// </remarks>
public sealed class SetTheAppearance(
    ICallerIdentity caller, IInstanceAppearance appearances, TimeProvider clock)
{
    public async Task<InstanceAppearance> ExecuteAsync(
        string? title,
        MarkColour colour,
        MarkShape shape,
        ContentVersion held,
        CancellationToken cancellationToken)
    {
        caller.Caller.RequireOwner("name this instance");

        var appearance = await appearances.ReadAsync(cancellationToken);

        if (!appearance.Version.Matches(held))
        {
            throw Refusal.Stale(
                "The appearance has changed since it was read. Read it again: the write you sent "
                + "would have replaced somebody else's newer one.",
                appearance.Version);
        }

        if (appearance.Set(title, colour, shape, clock.GetUtcNow()))
        {
            await appearances.SaveAsync(cancellationToken);
        }

        return appearance;
    }
}
