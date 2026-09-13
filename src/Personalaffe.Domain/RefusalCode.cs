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
/// The set grows with the epics that need it — <c>deleted</c> with recoverable
/// deletion, <c>disabled</c> with the application switch — and each addition is
/// a line in <c>docs/api.md</c> in the same commit.
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

    /// <summary>The object has changed since it was read.</summary>
    Stale,

    /// <summary>Something else already occupies the name or the place.</summary>
    Conflict,

    /// <summary>Something went wrong on the server, and the caller is told no more than that.</summary>
    Internal,
}
