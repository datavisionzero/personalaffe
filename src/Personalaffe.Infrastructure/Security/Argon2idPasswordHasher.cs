using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Personalaffe.Application.Ports;

namespace Personalaffe.Infrastructure.Security;

/// <summary>
/// Argon2id, in a self-describing value that carries the parameters it was made
/// with.
/// </summary>
/// <remarks>
/// <para>
/// The encoded form is the PHC one — <c>$argon2id$v=19$m=…,t=…,p=…$salt$hash</c>
/// — and verification reads its parameters out of the stored value rather than
/// out of the constants below. That is what lets the cost be raised later
/// without locking out the owner who was set up under the old one.
/// </para>
/// <para>
/// The parameters are read back with bounds, because they decide how much work
/// this process does and the value they come from is a row in a database. A
/// tampered row that asked for four gigabytes would otherwise be a way to stop
/// the instance by writing to it.
/// </para>
/// <para>
/// Nothing here throws for a value it cannot read. An encoding this hasher does
/// not understand admits nobody, which is what the door wants; an exception out
/// of the door would be a 500 where the answer is "no".
/// </para>
/// </remarks>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const int MemoryKiB = 65536;
    private const int Iterations = 3;
    private const int Parallelism = 1;
    private const int HashBytes = 32;
    private const int SaltBytes = 16;

    public Task<string> HashAsync(string password, CancellationToken cancellationToken) =>
        EncodeAsync(
            Domain.Password.Checked(password),
            RandomNumberGenerator.GetBytes(SaltBytes),
            MemoryKiB,
            Iterations,
            Parallelism,
            cancellationToken);

    public Task<string> HashAcceptedSecretAsync(string secret, CancellationToken cancellationToken) =>
        EncodeAsync(
            string.IsNullOrEmpty(secret)
                ? throw new ArgumentException("An accepted secret is required.", nameof(secret))
                : secret,
            RandomNumberGenerator.GetBytes(SaltBytes),
            MemoryKiB,
            Iterations,
            Parallelism,
            cancellationToken);

    public async Task<bool> VerifyAsync(
        string encodedHash, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(encodedHash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            var parts = encodedHash.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
            {
                return false;
            }

            var parameters = parts[3]
                .Split(',')
                .Select(pair => pair.Split('='))
                .ToDictionary(pair => pair[0], pair => int.Parse(pair[1], CultureInfo.InvariantCulture));

            var salt = Convert.FromBase64String(parts[4]);
            var expected = Convert.FromBase64String(parts[5]);

            if (parameters["m"] is < 8192 or > 262144
                || parameters["t"] is < 1 or > 10
                || parameters["p"] is < 1 or > 16
                || salt.Length is < 16 or > 64
                || expected.Length != HashBytes)
            {
                return false;
            }

            var recomputed = await EncodeAsync(
                password, salt, parameters["m"], parameters["t"], parameters["p"], cancellationToken);

            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(recomputed.Split('$')[5]), expected);
        }
        catch (Exception readable) when (
            readable is FormatException or KeyNotFoundException or OverflowException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    private static async Task<string> EncodeAsync(
        string password,
        byte[] salt,
        int memory,
        int iterations,
        int parallelism,
        CancellationToken cancellationToken)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        cancellationToken.ThrowIfCancellationRequested();
        var hash = await argon.GetBytesAsync(HashBytes);
        cancellationToken.ThrowIfCancellationRequested();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={memory},t={iterations},p={parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }
}
