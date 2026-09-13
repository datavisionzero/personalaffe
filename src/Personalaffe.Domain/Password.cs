namespace Personalaffe.Domain;

/// <summary>
/// What personalaffe asks of the owner's password, which is length and nothing
/// else.
/// </summary>
/// <remarks>
/// <para>
/// No character classes and no forced rotation. Both push a person towards a
/// short password with a digit stuck on the end, and the thing that actually
/// protects one owner's workspace is length, a slow hash and a throttle on
/// failed attempts — the second is <c>IPasswordHasher</c>'s and the third is
/// the door's.
/// </para>
/// <para>
/// The maximum is here because the hash is deliberately expensive: without one,
/// a megabyte of text in the field would be a megabyte of Argon2id, at the
/// caller's choosing and on the instance's clock.
/// </para>
/// </remarks>
public static class Password
{
    public const int MinLength = 12;

    public const int MaxLength = 200;

    /// <summary>The password itself, once it is one.</summary>
    /// <exception cref="Refusal">It is missing, too short or too long.</exception>
    public static string Checked(string? password, string field = "password")
    {
        if (string.IsNullOrEmpty(password))
        {
            throw Refusal.Validation(field, "A password is required.");
        }

        return password.Length is < MinLength or > MaxLength
            ? throw Refusal.Validation(
                field, $"A password is {MinLength} to {MaxLength} characters.")
            : password;
    }
}
