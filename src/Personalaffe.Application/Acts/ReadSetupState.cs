using Personalaffe.Application.Ports;

namespace Personalaffe.Application.Acts;

/// <summary>What a caller who has not signed in may know: whether there is an owner yet.</summary>
public sealed record SetupState(bool Required);

/// <summary>
/// Whether this instance still needs its one-time setup.
/// </summary>
/// <remarks>
/// It is outside the door because it has to be: a browser arriving at a fresh
/// installation cannot sign in and has to be told to set up instead. It is also
/// the whole of what it says. Who the owner is, when they were set up and what
/// address they use are not in the answer, because an instance on the public
/// internet answers this to whoever asks.
/// </remarks>
public sealed class ReadSetupState(IOwners owners)
{
    public async Task<SetupState> ExecuteAsync(CancellationToken cancellationToken) =>
        new(!await owners.ExistsAsync(cancellationToken));
}
