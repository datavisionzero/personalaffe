using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>Who deleted something, as the Trash shows it.</summary>
public sealed record ActorShape(CallerKind Kind, string? Name)
{
    public static ActorShape Of(Actor actor) => new(actor.Kind, actor.Name);
}

/// <summary>One thing in the Trash.</summary>
public sealed record TrashEntryResponse(
    WorkspaceApplication Application,
    Guid Id,
    string Name,
    string? Where,
    DateTimeOffset DeletedAt,
    ActorShape DeletedBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset UpdatedAt)
{
    public static TrashEntryResponse Of(TrashEntry entry) => new(
        entry.Application,
        entry.Id,
        entry.Name,
        entry.Where,
        entry.DeletedAt,
        ActorShape.Of(entry.DeletedBy),
        entry.ExpiresAt,
        entry.UpdatedAt);
}

/// <summary>What is in the Trash, and whether the limit cut it short.</summary>
public sealed record TrashResponse(IReadOnlyList<TrashEntryResponse> Items, bool HasMore);

/// <summary>How much emptying it removed.</summary>
public sealed record TrashEmptiedResponse(int Removed);

/// <summary>
/// The Trash (<c>docs/api.md</c>, The Trash): one surface over the four
/// applications, and no table underneath it that stands for another table.
/// </summary>
/// <remarks>
/// <para>
/// Reading is filtered by what the caller may read, and restoring needs write
/// access to the application it is in. <strong>Removing something for good is
/// the owner's alone</strong>, singly and in bulk: an agent that could
/// permanently remove one entry could bypass the Trash in two steps instead of
/// one.
/// </para>
/// <para>
/// The four applications are PERSONAL-E5 to PERSONAL-E8 and none of them
/// exists yet, so an instance today answers an empty Trash. That is the shape
/// working, not the shape missing: a contributor is the whole of what a content
/// module adds to appear here.
/// </para>
/// </remarks>
public static class TrashEndpoints
{
    public static IEndpointRouteBuilder MapTrash(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/trash", async (
                string? application,
                int? limit,
                ReadTheTrash act,
                CancellationToken cancellationToken) =>
            {
                var trash = await act.ExecuteAsync(
                    Applications.Chosen(application), limit, cancellationToken);

                return Results.Ok(new TrashResponse([.. trash.Items.Select(TrashEntryResponse.Of)], trash.HasMore));
            })
            .WithName("ReadTrash")
            .WithSummary("What the caller deleted and may still see, newest deletion first.")
            .Produces<TrashResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .NamesAnApplication();

        endpoints.MapPost("/trash/{application}/{id:guid}/restore", async (
                string application,
                Guid id,
                string? name,
                HttpRequest request,
                RestoreFromTheTrash act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(
                    Applications.Named(application), id, EntityTags.Required(request), name, cancellationToken);

                return Results.NoContent();
            })
            .WithName("RestoreFromTrash")
            .WithSummary("Put one thing back where it came from, or under another name.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .NamesAnApplication()
            .Guarded();

        endpoints.MapDelete("/trash/{application}/{id:guid}", async (
                string application,
                Guid id,
                HttpRequest request,
                RemoveFromTheTrash act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(
                    Applications.Named(application), id, EntityTags.Required(request), cancellationToken);

                return Results.NoContent();
            })
            .WithName("RemoveFromTrash")
            .WithSummary("Remove one thing for good. The owner's alone.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .NamesAnApplication()
            .Guarded();

        endpoints.MapDelete("/trash", async (
                string? application,
                EmptyTheTrash act,
                CancellationToken cancellationToken) =>
            {
                var removed = await act.ExecuteAsync(
                    Applications.Chosen(application), cancellationToken);

                return Results.Ok(new TrashEmptiedResponse(removed));
            })
            .WithName("EmptyTrash")
            .WithSummary("Remove everything in it for good. The owner's alone.")
            .Produces<TrashEmptiedResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .NamesAnApplication();

        return endpoints;
    }
}
