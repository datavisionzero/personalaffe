namespace Personalaffe.Domain;

/// <summary>
/// Every way personalaffe says no, as one closed set.
/// </summary>
/// <remarks>
/// <para>
/// It is here rather than in the Api because it is the product's vocabulary and
/// not HTTP's: the web application switches on it, the CLI derives an exit code
/// from it, and both would otherwise be reading a status code and guessing.
/// What each one is in HTTP is <c>Http/Problems.cs</c>, and that is the only
/// place that knows.
/// </para>
/// <para>
/// The set grows with the epics that need it, and each addition is a line in
/// <c>docs/api.md</c> in the same commit. <c>disabled</c> arrived with the
/// application switch of PERSONAL-E4; <c>too-large</c> and <c>out-of-space</c>
/// with the two limits of PERSONAL-E6.
/// </para>
/// </remarks>
public enum RefusalCode
{
    /// <summary>A field is missing, malformed or over its limit.</summary>
    Validation,

    /// <summary>The request carries a field the object does not define.</summary>
    UnknownField,

    /// <summary>No credential, an unknown one, or a revoked one.</summary>
    Unauthenticated,

    /// <summary>
    /// The password was right and the authenticator's code is still wanted.
    /// </summary>
    SecondFactor,

    /// <summary>The caller may not do this.</summary>
    Forbidden,

    /// <summary>Nothing by that address.</summary>
    NotFound,

    /// <summary>
    /// The application this belongs to is switched off. What is in it is kept;
    /// the owner switching it back on is what makes it reachable again.
    /// </summary>
    Disabled,

    /// <summary>
    /// What used to be at that address is in the Trash, and can be brought
    /// back. A 404 like <see cref="NotFound"/>, told apart by its type.
    /// </summary>
    Deleted,

    /// <summary>The object has changed since it was read.</summary>
    Stale,

    /// <summary>Something else already occupies the name or the place.</summary>
    Conflict,

    /// <summary>
    /// What was sent is larger than this instance will store. The caller's move
    /// is to send something smaller.
    /// </summary>
    TooLarge,

    /// <summary>
    /// This instance has no room left. The caller's move is to delete
    /// something, which is why it is not the same code as
    /// <see cref="TooLarge"/>.
    /// </summary>
    OutOfSpace,

    /// <summary>Something went wrong on the server, and the caller is told no more than that.</summary>
    Internal,
}
