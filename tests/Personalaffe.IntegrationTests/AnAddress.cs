using System.Text.RegularExpressions;
using Personalaffe.Api.Http;
using Personalaffe.Domain;
using Personalaffe.Domain.Dashboard;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// A path out of the contract, with its parameters filled in.
/// </summary>
/// <remarks>
/// <para>
/// The suites that walk every operation in
/// <c>docs/api/openapi.json</c> — the door's and the safeguards' — send a
/// request to each one, and what they are asking is who may ask rather than
/// whether the thing exists. So every parameter is filled with something that
/// names nothing.
/// </para>
/// <para>
/// <strong>Every parameter, and not a list of the ones that exist today.</strong>
/// A placeholder left in the path is a segment that matches no route, so the
/// request lands on the group's own not-found — which is outside the door on
/// purpose — and the check passes without ever having reached the endpoint it
/// is about. That is how PERSONAL-E7's two revision addresses first read as
/// green, and this is the shared answer so that the next epic's parameter
/// cannot do it again.
/// </para>
/// </remarks>
internal static partial class AnAddress
{
    internal static string Filled(string path, Guid? id = null) =>
        ThePlaceholder().Replace(
            path,
            match => match.Groups[1].Value switch
            {
                // The two parameters that are not ids: closed sets of words,
                // and any of them will do. A guid here would be refused as
                // `validation` before the endpoint's own guard was reached,
                // and the check would read as green for the wrong reason.
                Applications.Parameter => WorkspaceApplication.Knowledge.Wire(),
                Tiles.Parameter => DashboardTile.Knowledge.Wire(),
                "id" => (id ?? Guid.CreateVersion7()).ToString(),
                _ => Guid.CreateVersion7().ToString(),
            });

    [GeneratedRegex(@"\{([a-z]+)\}")]
    private static partial Regex ThePlaceholder();
}
