using Personalaffe.Application.Acts.Search;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Search;

namespace Personalaffe.Api.Http;

/// <summary>One thing the search found.</summary>
/// <param name="Snippet">
/// The piece of the body the words were found in, as text and not as markup:
/// nothing marks the matched words up, so nothing downstream has to decide
/// whether this is safe to render. A client that wants them marked has the
/// words it asked with.
/// </param>
/// <param name="Within">
/// What it sits in — a page's parent, a task's list, a file's folder — so that
/// a client can open the screen it is on. Nothing for a Scratchpad entry, and
/// nothing at the top of a tree.
/// </param>
/// <param name="Rank">
/// How well it matched. Bigger is better and the list is already in that order;
/// it is here so that a client merging two answers can keep it.
/// </param>
public sealed record FoundResponse(
    WorkspaceApplication Application,
    Guid Id,
    string Title,
    string? Snippet,
    Guid? Within,
    DateTimeOffset UpdatedAt,
    double Rank,
    string? TargetUrl = null)
{
    public static FoundResponse Of(Found found) => new(
        found.Application,
        found.Id,
        found.Title,
        found.Snippet,
        found.Within,
        found.UpdatedAt,
        found.Rank,
        found.TargetUrl);
}

/// <summary>
/// What one search found, and whether the limit cut it short.
/// </summary>
/// <remarks>
/// Not <c>SearchResponse</c>: an operation called <c>Search</c> makes a generated
/// client name its own response type that, and two types with one name is a
/// client that does not compile. <c>ContractTests</c> is what keeps the next one
/// from happening.
/// </remarks>
public sealed record FindingsResponse(
    string Query, IReadOnlyList<FoundResponse> Items, bool HasMore);

/// <summary>
/// One search over the four applications (<c>docs/api.md</c>, The search).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One endpoint, because it is one question.</strong> "Where did I
/// write that" is not four questions about four applications, and a client that
/// had to ask each of them would be the one deciding how a page ranks against a
/// task.
/// </para>
/// <para>
/// <strong>It is an aggregate view and it filters rather than refuses.</strong>
/// An application this caller cannot read, or one the owner has switched off,
/// contributes nothing and produces no refusal — the same behaviour
/// <c>GET /api/trash</c> has, for the same reason.
/// </para>
/// <para>
/// <strong>What is in a file is never looked at.</strong> Files contribute
/// their names (VISION §6.1), which is the line between one search over a
/// workspace and a document search over a disk.
/// </para>
/// </remarks>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/search", async (
                string? q,
                string? application,
                int? limit,
                SearchTheWorkspace act,
                CancellationToken cancellationToken) =>
            {
                var findings = await act.ExecuteAsync(
                    q, Applications.Chosen(application), limit, cancellationToken);

                return Results.Ok(new FindingsResponse(
                    findings.Needle,
                    [.. findings.Items.Select(FoundResponse.Of)],
                    findings.HasMore));
            })
            .WithName("Search")
            .WithSummary("Find something in the applications this caller can read.")
            .WithDescription(
                "Every word is matched as a beginning and all of them have to be found, so `arch dec` "
                + $"finds \"Architecture decisions\". At most {Needle.MaxWords} words are used and "
                + $"single letters are dropped. At most {SearchTheWorkspace.MaxLimit} findings come "
                + $"back, {SearchTheWorkspace.DefaultLimit} where no limit is asked for.")
            .Produces<FindingsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .NamesAnApplication();

        return endpoints;
    }
}
