namespace Personalaffe.Application.Ports;

/// <summary>
/// What turns a password into something storable, and what checks one against
/// what was stored.
/// </summary>
/// <remarks>
/// A port rather than a call, because the algorithm and its cost are
/// Infrastructure's to choose and to raise later. The encoded value is
/// self-describing: it carries the parameters it was made with, so raising the
/// cost does not invalidate the owner that exists.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>The encoded hash of <paramref name="password"/>.</summary>
    Task<string> HashAsync(string password, CancellationToken cancellationToken);

    /// <summary>
    /// The encoded hash of a chosen secret whose own domain rule has already
    /// accepted it. This is the same slow, salted primitive as a password,
    /// without imposing the password's length rule on another kind of secret.
    /// </summary>
    Task<string> HashAcceptedSecretAsync(string secret, CancellationToken cancellationToken) =>
        HashAsync(secret, cancellationToken);

    /// <summary>
    /// Whether <paramref name="password"/> is the one behind
    /// <paramref name="encodedHash"/>. An encoding this hasher cannot read is
    /// <c>false</c> and never an exception: a row that has been tampered with
    /// admits nobody rather than crashing the door.
    /// </summary>
    Task<bool> VerifyAsync(string encodedHash, string password, CancellationToken cancellationToken);
}
