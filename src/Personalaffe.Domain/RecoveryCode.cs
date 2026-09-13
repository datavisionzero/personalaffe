using System.Security.Cryptography;
using System.Text;

namespace Personalaffe.Domain;

/// <summary>
/// One of the codes the owner keeps for the day the authenticator is gone: used
/// once, stored as a digest, and shown exactly once.
/// </summary>
/// <remarks>
/// <para>
/// A phone is lost, dropped or wiped, and personalaffe has no mail server to
/// send a link through. These are what stands between that and the procedure on
/// the server, and the only thing they have to be is written down somewhere
/// that is not the phone.
/// </para>
/// <para>
/// Ten of them, each five bits per character of randomness in a group of ten
/// characters. The dash is for the person copying them out; nothing about the
/// value depends on it, and <see cref="Normalize"/> takes it back out.
/// </para>
/// </remarks>
public sealed class RecoveryCode
{
    /// <summary>How many are issued at once.</summary>
    public const int Count = 10;

    /// <summary>Characters per code, before the dash a person reads it by.</summary>
    public const int Length = 10;

    /// <summary>
    /// No <c>I</c>, <c>L</c>, <c>O</c>, <c>U</c> or digits that look like them:
    /// this is a string somebody types off paper, months later, probably in a
    /// hurry.
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    private RecoveryCode()
    {
        // EF Core materializes through this; every other route goes through Issue.
    }

    private RecoveryCode(Guid ownerId, byte[] codeHash, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7(at);
        OwnerId = ownerId;
        CodeHash = codeHash;
        CreatedAt = at;
    }

    public Guid Id { get; private init; }

    public Guid OwnerId { get; private init; }

    public byte[] CodeHash { get; private init; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When it was spent. A code is good once.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public bool Used => UsedAt is not null;

    /// <summary>
    /// A fresh set and the text of each, which the caller shows once and then
    /// cannot recover.
    /// </summary>
    public static (IReadOnlyList<RecoveryCode> Codes, IReadOnlyList<string> Text) Issue(
        Guid ownerId, DateTimeOffset at)
    {
        var text = new List<string>(Count);
        var codes = new List<RecoveryCode>(Count);

        for (var issued = 0; issued < Count; issued++)
        {
            var value = RandomNumberGenerator.GetString(Alphabet, Length);

            text.Add($"{value[..(Length / 2)]}-{value[(Length / 2)..]}");
            codes.Add(new RecoveryCode(ownerId, Hash(value), at));
        }

        return (codes, text);
    }

    /// <summary>The digest a presented code is looked up by.</summary>
    public static byte[] Hash(string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)));

    /// <summary>
    /// The code as it is compared: upper case, and without the dash or the
    /// spaces somebody typed around it.
    /// </summary>
    public static string Normalize(string? code) =>
        new((code ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character))
            .Select(char.ToUpperInvariant)
            .ToArray());

    /// <summary>Spending it. A code that was already spent stays spent at the moment it was.</summary>
    public void Use(DateTimeOffset at) => UsedAt ??= at;
}
