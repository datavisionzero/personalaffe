using Personalaffe.Application.Acts.Knowledge;

namespace Personalaffe.Api.Http;

/// <summary>One page, with what it says.</summary>
public sealed record PageResponse(
    Guid Id,
    string Title,
    Guid? Parent,
    string Markdown,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PageResponse Of(ThePage page) => new(
        page.Id, page.Title, page.Parent, page.Markdown, page.CreatedAt, page.UpdatedAt);
}

/// <summary>One page as the tree knows it: a title and a place, and no body.</summary>
public sealed record OutlineResponse(
    Guid Id, string Title, Guid? Parent, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static OutlineResponse Of(TheOutline page) => new(
        page.Id, page.Title, page.Parent, page.CreatedAt, page.UpdatedAt);
}

/// <summary>
/// The whole hierarchy, flat: each page says which one it is under, and a
/// client draws the tree in one pass.
/// </summary>
public sealed record TreeResponse(IReadOnlyList<OutlineResponse> Pages);

/// <summary>One previous version, as a history lists it.</summary>
public sealed record RevisionResponse(Guid Id, string Title, DateTimeOffset At, ActorShape By)
{
    public static RevisionResponse Of(TheRevision revision) =>
        new(revision.Id, revision.Title, revision.At, ActorShape.Of(revision.By));
}

/// <summary>What a page used to say, newest first.</summary>
public sealed record HistoryResponse(IReadOnlyList<RevisionResponse> Items);

/// <summary>One previous version, with what it said.</summary>
public sealed record OldVersionResponse(
    Guid Id, string Title, string Markdown, DateTimeOffset At, ActorShape By)
{
    public static OldVersionResponse Of(TheOldVersion version) => new(
        version.Id, version.Title, version.Markdown, version.At, ActorShape.Of(version.By));
}

/// <summary>A page to write: what it is called, where it goes, what it says.</summary>
public sealed record WritePageRequest(string? Title, Guid? Parent, string? Markdown);

/// <summary>
/// What a page is being changed to — the title, the place and the body
/// together, because all three are the same row.
/// </summary>
public sealed record RewritePageRequest(string? Title, Guid? Parent, string? Markdown);

/// <summary>
/// Knowledge (<c>docs/api.md</c>, Knowledge): the owner's lasting notes, as
/// Markdown in a tree, with a history behind every page.
/// </summary>
/// <remarks>
/// <para>
/// Nine addresses, and every act behind them opens with
/// <c>ReachingAnApplication</c>: access first, then the switch.
/// </para>
/// <para>
/// <strong>The address of a page is its id.</strong> It is made once and never
/// changes, so a link written down keeps working through every rename, move,
/// rewrite and recovery — which is the whole of what <c>docs/mvp-plan.md</c>
/// means by stable links, and the same decision Files made about a file's
/// bytes.
/// </para>
/// </remarks>
public static class KnowledgeEndpoints
{
    private const string Pages = "/knowledge/pages";

    public static IEndpointRouteBuilder MapKnowledge(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Pages, async (ReadTheTree act, CancellationToken cancellationToken) =>
            {
                var tree = await act.ExecuteAsync(cancellationToken);

                return Results.Ok(new TreeResponse([.. tree.Pages.Select(OutlineResponse.Of)]));
            })
            .WithName("ReadTree")
            .WithSummary("Every page's title and place, and nothing any of them says.")
            .Produces<TreeResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost(Pages, async (
                WritePageRequest body,
                HttpResponse response,
                WriteANewPage act,
                CancellationToken cancellationToken) =>
            {
                var page = await act.ExecuteAsync(body.Title, body.Parent, body.Markdown, cancellationToken);

                EntityTags.Write(response, page.Version);

                return Results.Created($"{Routes.Api}{Pages}/{page.Id}", PageResponse.Of(page));
            })
            .WithName("WritePage")
            .WithSummary("Write a page.")
            .Produces<PageResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet($"{Pages}/{{id:guid}}", async (
                Guid id,
                HttpResponse response,
                ReadAPage act,
                CancellationToken cancellationToken) =>
            {
                var page = await act.ExecuteAsync(id, cancellationToken);

                EntityTags.Write(response, page.Version);

                return Results.Ok(PageResponse.Of(page));
            })
            .WithName("ReadPage")
            .WithSummary("One page, and the version a write on it replaces.")
            .Produces<PageResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPut($"{Pages}/{{id:guid}}", async (
                Guid id,
                RewritePageRequest body,
                HttpRequest request,
                HttpResponse response,
                RewriteAPage act,
                CancellationToken cancellationToken) =>
            {
                var page = await act.ExecuteAsync(
                    id, body.Title, body.Parent, body.Markdown, EntityTags.Required(request), cancellationToken);

                EntityTags.Write(response, page.Version);

                return Results.Ok(PageResponse.Of(page));
            })
            .WithName("RewritePage")
            .WithSummary("Change a page's title, its place, its Markdown, or any of them.")
            .Produces<PageResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapDelete($"{Pages}/{{id:guid}}", async (
                Guid id,
                HttpRequest request,
                DiscardAPage act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(id, EntityTags.Required(request), cancellationToken);

                return Results.NoContent();
            })
            .WithName("DiscardPage")
            .WithSummary("Put a page in the Trash, with everything under it and all of its history.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapGet($"{Pages}/{{id:guid}}/revisions", async (
                Guid id,
                ReadTheHistory act,
                CancellationToken cancellationToken) =>
            {
                var history = await act.ExecuteAsync(id, cancellationToken);

                return Results.Ok(new HistoryResponse([.. history.Select(RevisionResponse.Of)]));
            })
            .WithName("ReadHistory")
            .WithSummary("What a page used to say, newest first. Without the bodies.")
            .Produces<HistoryResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet($"{Pages}/{{id:guid}}/revisions/{{revision:guid}}", async (
                Guid id,
                Guid revision,
                ReadAnOldVersion act,
                CancellationToken cancellationToken) =>
            {
                var version = await act.ExecuteAsync(id, revision, cancellationToken);

                return Results.Ok(OldVersionResponse.Of(version));
            })
            .WithName("ReadOldVersion")
            .WithSummary("One previous version, with what it said.")
            .Produces<OldVersionResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost($"{Pages}/{{id:guid}}/revisions/{{revision:guid}}", async (
                Guid id,
                Guid revision,
                HttpRequest request,
                HttpResponse response,
                RecoverARevision act,
                CancellationToken cancellationToken) =>
            {
                var page = await act.ExecuteAsync(
                    id, revision, EntityTags.Required(request), cancellationToken);

                EntityTags.Write(response, page.Version);

                return Results.Ok(PageResponse.Of(page));
            })
            .WithName("RecoverRevision")
            .WithSummary("Put a previous version back. What is current now becomes one of its own.")
            .Produces<PageResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapGet("/knowledge/export", async (
                HttpResponse response,
                ExportTheKnowledge act,
                CancellationToken cancellationToken) =>
            {
                var export = await act.ExecuteAsync(cancellationToken);

                // An attachment, like a stored file's bytes and for the same
                // reason: what leaves this instance as a document is never a
                // document of this instance's own origin.
                response.Headers.XContentTypeOptions = "nosniff";

                return Results.File(export.Bytes, "application/zip", export.Name);
            })
            .WithName("ExportKnowledge")
            .WithSummary("The whole knowledge base, as a zip of Markdown files.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AnswersAZip();

        return endpoints;
    }
}
