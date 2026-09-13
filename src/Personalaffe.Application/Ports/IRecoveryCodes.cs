using Personalaffe.Domain;

namespace Personalaffe.Application.Ports;

/// <summary>The owner's recovery codes, as the acts need them.</summary>
public interface IRecoveryCodes
{
    /// <summary>
    /// Puts <paramref name="codes"/> in place of every code the owner had.
    /// Issuing a new set is what invalidates the old one — two sets in force at
    /// once would mean a sheet of paper somebody threw away still works.
    /// </summary>
    Task ReplaceAsync(Guid ownerId, IReadOnlyList<RecoveryCode> codes, CancellationToken cancellationToken);

    /// <summary>How many are left unspent.</summary>
    Task<int> RemainingAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Spends the code behind <paramref name="codeHash"/> and says whether
    /// there was one to spend. A code already spent is not one.
    /// </summary>
    Task<bool> ConsumeAsync(
        Guid ownerId, byte[] codeHash, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Takes every code away, which is what disabling the second factor does.</summary>
    Task ClearAsync(Guid ownerId, CancellationToken cancellationToken);
}
