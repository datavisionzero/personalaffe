using System.Security.Cryptography;
using System.Text;

namespace Personalaffe.Domain;

/// <summary>
/// The secret half of an agent's token: what is generated, what is shown once,
/// and what the row keeps of it.
/// </summary>
/// <remarks>
/// <para>
/// <c>pea_</c> and then 43 characters from <c>[A-Za-z0-9]</c> — 256 bits — made
/// by the instance and never by a caller. The prefix is what makes one
/// recognisable in a log, a shell history or a paste, so that whoever finds it
/// knows what they have found and where to revoke it.
/// </para>
/// <para>
/// What is stored is the SHA-256 and the first eight characters. A plain digest
/// rather than a slow hash, deliberately: the secret has 256 bits of entropy
/// and is generated rather than chosen, so there is nothing here for a slow
/// hash to protect — and this is looked up on every request an agent makes.
/// </para>
/// </remarks>
public static class TokenSecret
{
    /// <summary>What every token starts with, after the CLI it is usually held by.</summary>
    public const string Prefix = "pea_";

    public const int RandomLength = 43;

    /// <summary>What is shown in a list so that two tokens can be told apart.</summary>
    public const int PrefixLength = 12;

    /// <summary>The length of a SHA-256 digest.</summary>
    public const int HashLength = 32;

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>The whole length of a generated secret.</summary>
    public static int Length => Prefix.Length + RandomLength;

    public static string Generate() => Prefix + RandomNumberGenerator.GetString(Alphabet, RandomLength);

    public static byte[] HashOf(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>
    /// The recognisable head of a secret — <c>pea_</c> and the first characters
    /// after it — which is all a list ever shows.
    /// </summary>
    public static string PrefixOf(string secret) => Checked(secret)[..PrefixLength];

    /// <summary>
    /// Whether something could be one of ours, decided before the database is
    /// asked. A header that is not this shape never becomes a query.
    /// </summary>
    public static bool IsAcceptable(string? secret) =>
        secret is not null
        && secret.Length == Length
        && secret.StartsWith(Prefix, StringComparison.Ordinal);

    /// <exception cref="ArgumentException"><paramref name="secret"/> is not one of ours.</exception>
    public static string Checked(string secret) =>
        IsAcceptable(secret)
            ? secret
            : throw new ArgumentException(
                $"A token is {Prefix} and {RandomLength} characters.", nameof(secret));
}
