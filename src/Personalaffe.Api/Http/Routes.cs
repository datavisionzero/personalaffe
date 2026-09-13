namespace Personalaffe.Api.Http;

/// <summary>
/// Where the API is, as one word (<c>docs/codebase.md</c>). Every endpoint
/// hangs under <see cref="Api"/> and every other path is the web
/// application's, which is what keeps the two from fighting over
/// <c>/files</c>, <c>/tasks</c> and <c>/pages</c> — addresses both worlds want.
/// </summary>
/// <remarks>
/// Routing applies the prefix by itself, because the endpoints are mapped into
/// a group carrying it. What needs this constant is everything routing does not
/// write: the <c>Location</c> of a created object, and the address of the
/// contract.
/// </remarks>
public static class Routes
{
    /// <summary>The first segment of every address the instance answers as an API.</summary>
    public const string Api = "/api";
}
