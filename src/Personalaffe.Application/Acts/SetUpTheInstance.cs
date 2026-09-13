using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Application.Acts;

/// <summary>
/// The one-time setup: an instance that has no owner acquires the one it will
/// have.
/// </summary>
/// <remarks>
/// <para>
/// This is the only way an owner is ever created, and it works exactly once.
/// A second attempt is <c>conflict</c> — not <c>forbidden</c>, because nothing
/// about the caller is wrong; the instance is simply already somebody's. There
/// is no invitation, no second account and no surface that adds one: the check
/// here is the ordinary answer and the unique index behind
/// <see cref="IOwners.AddAsync"/> is what holds when two callers ask in the
/// same moment.
/// </para>
/// <para>
/// The password is hashed before the row is written and is not kept anywhere
/// else, not for the length of this call: what is passed on is the encoded
/// hash.
/// </para>
/// </remarks>
public sealed class SetUpTheInstance(IOwners owners, IPasswordHasher passwords, TimeProvider clock)
{
    /// <exception cref="Refusal">
    /// The address or the password is not acceptable (<c>validation</c>), or
    /// this instance already has an owner (<c>conflict</c>).
    /// </exception>
    public async Task<Owner> ExecuteAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        // Both are checked before anything expensive happens: a password that
        // is too short must not cost an Argon2id, and a caller who sent neither
        // field should hear about both.
        var errors = new Dictionary<string, string[]>();
        string? address = null;

        try
        {
            address = Owner.NormalizeEmail(email);
        }
        catch (Refusal refusal) when (refusal.Code == RefusalCode.Validation)
        {
            Collect(errors, refusal);
        }

        try
        {
            Password.Checked(password);
        }
        catch (Refusal refusal) when (refusal.Code == RefusalCode.Validation)
        {
            Collect(errors, refusal);
        }

        if (errors.Count > 0)
        {
            throw Refusal.Validation(errors);
        }

        if (await owners.ExistsAsync(cancellationToken))
        {
            throw AlreadySetUp();
        }

        var owner = Owner.Claim(
            address!,
            await passwords.HashAsync(password!, cancellationToken),
            clock.GetUtcNow());

        await owners.AddAsync(owner, cancellationToken);

        return owner;
    }

    /// <summary>
    /// The one sentence a second setup gets, said in one place so that the
    /// race and the ordinary case cannot answer differently.
    /// </summary>
    public static Refusal AlreadySetUp() => Refusal.Conflict(
        "This instance already has an owner. There is one owner and there is no second account; "
        + "an owner who has lost their password recovers on the machine that runs it.");

    private static void Collect(Dictionary<string, string[]> errors, Refusal refusal)
    {
        if (refusal.Extensions.TryGetValue("errors", out var value)
            && value is IReadOnlyDictionary<string, string[]> fields)
        {
            foreach (var (field, messages) in fields)
            {
                errors[field] = messages;
            }
        }
    }
}
