namespace Personalaffe.Domain;

/// <summary>
/// RFC 4648 base32, which is the alphabet an authenticator app reads a shared
/// secret in.
/// </summary>
/// <remarks>
/// Here rather than from a package: it is thirty lines, it is the only
/// encoding this product needs that the base class library does not have, and
/// what it is used for — the one string an owner may have to type into a phone
/// by hand — is a rule of the product rather than a detail of a library.
/// </remarks>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Without padding: an <c>otpauth://</c> URI carries none.</summary>
    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var encoded = new System.Text.StringBuilder((bytes.Length * 8 / 5) + 1);

        var buffer = 0;
        var bits = 0;

        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                encoded.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            encoded.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return encoded.ToString();
    }

    /// <summary>
    /// The bytes behind an encoded secret. Padding, spaces and lower case are
    /// all accepted, because the string is one a person may retype.
    /// </summary>
    /// <exception cref="ArgumentException">It is not base32.</exception>
    public static byte[] Decode(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        var bytes = new List<byte>(encoded.Length * 5 / 8);

        var buffer = 0;
        var bits = 0;

        foreach (var character in encoded)
        {
            if (character is '=' or ' ' or '-')
            {
                continue;
            }

            var value = Alphabet.IndexOf(char.ToUpperInvariant(character), StringComparison.Ordinal);
            if (value < 0)
            {
                throw new ArgumentException("A shared secret is base32.", nameof(encoded));
            }

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 255));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
