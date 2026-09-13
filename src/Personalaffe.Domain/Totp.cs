using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Personalaffe.Domain;

/// <summary>
/// The second factor: a time-based one-time password to RFC 6238, which is what
/// every authenticator app on a phone already speaks.
/// </summary>
/// <remarks>
/// <para>
/// SHA-1, six digits and thirty-second steps, because those are the defaults
/// every authenticator assumes and several of them cannot be told otherwise. A
/// stronger hash here would buy nothing — the secret is 160 bits of randomness
/// and the code lives for ninety seconds — and would cost the owner an app that
/// silently produces wrong codes.
/// </para>
/// <para>
/// One step either side is accepted, which is the window that forgives a phone
/// whose clock is a little out and a person who typed the last digit as the
/// minute turned. Wider than that starts to matter: every extra step is another
/// live code.
/// </para>
/// <para>
/// A code that has been used cannot be used again — the step it was issued for
/// is remembered, and anything at or below it is refused. Without that, a code
/// read over somebody's shoulder is good for the rest of its thirty seconds.
/// </para>
/// </remarks>
public static class Totp
{
    /// <summary>How long one code lasts.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    /// <summary>How many steps either side of now are accepted.</summary>
    public const int Tolerance = 1;

    public const int Digits = 6;

    /// <summary>160 bits, which is the size the algorithm's HMAC is built on.</summary>
    public const int SecretBytes = 20;

    /// <summary>A new shared secret, base32 as an authenticator reads it.</summary>
    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>
    /// The code <paramref name="secret"/> stands for at a given step, which is
    /// what verification compares against and what a test uses.
    /// </summary>
    public static string CodeFor(string secret, long step)
    {
        var counter = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);

        var mac = HMACSHA1.HashData(Base32.Decode(secret), counter);

        // Dynamic truncation, RFC 4226 §5.3: the low nibble of the last byte
        // says where in the digest to read the number from.
        var offset = mac[^1] & 0x0f;
        var truncated =
            ((mac[offset] & 0x7f) << 24)
            | (mac[offset + 1] << 16)
            | (mac[offset + 2] << 8)
            | mac[offset + 3];

        return (truncated % (int)Math.Pow(10, Digits))
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(Digits, '0');
    }

    /// <summary>Which step <paramref name="at"/> falls in.</summary>
    public static long StepAt(DateTimeOffset at) => at.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    /// <summary>
    /// Whether <paramref name="code"/> is a code of <paramref name="secret"/>
    /// at or around <paramref name="at"/>, and which step it was — so that the
    /// step can be remembered and the code cannot be used twice.
    /// </summary>
    /// <param name="after">
    /// The last step already used, if any. Anything at or below it is refused
    /// however correct it is.
    /// </param>
    public static bool Verify(string? secret, string? code, DateTimeOffset at, long? after, out long step)
    {
        step = 0;

        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var offered = code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (offered.Length != Digits || !offered.All(char.IsAsciiDigit))
        {
            return false;
        }

        var now = StepAt(at);

        for (var candidate = now - Tolerance; candidate <= now + Tolerance; candidate++)
        {
            if (after is { } used && candidate <= used)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(CodeFor(secret, candidate)), Encoding.ASCII.GetBytes(offered)))
            {
                step = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The <c>otpauth://</c> URI an authenticator is pointed at, usually as a
    /// QR code. The issuer is the product and the account is the owner's
    /// address, so that a phone holding several of these can tell them apart.
    /// </summary>
    public static string UriFor(string secret, string account)
    {
        var issuer = Uri.EscapeDataString("personalaffe");
        var label = Uri.EscapeDataString(account);

        return $"otpauth://totp/{issuer}:{label}?secret={secret}&issuer={issuer}"
            + $"&algorithm=SHA1&digits={Digits}&period={(int)Step.TotalSeconds}";
    }
}
