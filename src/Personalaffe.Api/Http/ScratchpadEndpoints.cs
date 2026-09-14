using Personalaffe.Application.Acts.Scratchpad;

namespace Personalaffe.Api.Http;

/// <summary>One Scratchpad entry.</summary>
/// <param name="ExpiresAt">
/// When the instance's own sweep will destroy it, or nothing at all while it is
/// pinned. It is the only warning there is: nothing is set aside and nothing
/// asks first.
/// </param>
public sealed record ScratchpadEntryResponse(
    Guid Id,
    string Text,
    bool Pinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt)
{
    public static ScratchpadEntryResponse Of(TheEntry entry) => new(
        entry.Id, entry.Text, entry.Pinned, entry.CreatedAt, entry.UpdatedAt, entry.ExpiresAt);
}

/// <summary>What is in the Scratchpad, and whether the limit cut it short.</summary>
public sealed record ScratchpadResponse(IReadOnlyList<ScratchpadEntryResponse> Items, bool HasMore);

/// <summary>Text put down, and whether it is exempt from expiry.</summary>
public sealed record CaptureEntryRequest(string? Text, bool Pinned = false);

/// <summary>
/// What an entry is being changed to — the text and the pin together.
/// </summary>
/// <remarks>
/// One shape for both, because a pin is a change to the entry and not an event
/// of its own. A second address carrying one boolean would be a second place the
/// guard has to be got right.
/// </remarks>
public sealed record RewriteEntryRequest(string? Text, bool Pinned = false);

/// <summary>
/// The Scratchpad (<c>docs/api.md</c>, The Scratchpad): temporary plain text,
/// captured in seconds and read on another device.
/// </summary>
/// <remarks>
/// <para>
/// Five endpoints, and every act behind them opens with
/// <c>ReachingAnApplication</c>: access first, then the switch. Nothing in this
/// module decides either for itself, and how it reads here is how the three
/// applications after it are written.
/// </para>
/// <para>
/// <strong>The delete means it.</strong> The row is destroyed, so a second
/// delete is <c>not-found</c>, <c>GET /api/trash</c> never carries a Scratchpad
/// entry however many have been deleted, and <c>deleted</c> is a code this
/// application never answers.
/// </para>
/// </remarks>
public static class ScratchpadEndpoints
{
    private const string Entries = "/scratchpad/entries";

    public static IEndpointRouteBuilder MapScratchpad(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Entries, async (
                int? limit,
                ReadTheEntries act,
                CancellationToken cancellationToken) =>
            {
                var scratchpad = await act.ExecuteAsync(limit, cancellationToken);

                return Results.Ok(new ScratchpadResponse(
                    [.. scratchpad.Items.Select(ScratchpadEntryResponse.Of)], scratchpad.HasMore));
            })
            .WithName("ReadScratchpad")
            .WithSummary("What is in the Scratchpad, newest capture first.")
            .Produces<ScratchpadResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost(Entries, async (
                CaptureEntryRequest body,
                HttpResponse response,
                CaptureAnEntry act,
                CancellationToken cancellationToken) =>
            {
                var entry = await act.ExecuteAsync(body.Text, body.Pinned, cancellationToken);

                // The version the capture produced, so that pinning or deleting
                // what was just written needs no read in between.
                EntityTags.Write(response, entry.Version);

                return Results.Created(
                    $"{Routes.Api}{Entries}/{entry.Id}", ScratchpadEntryResponse.Of(entry));
            })
            .WithName("CaptureEntry")
            .WithSummary("Put a piece of text down.")
            .Produces<ScratchpadEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet($"{Entries}/{{id:guid}}", async (
                Guid id,
                HttpResponse response,
                ReadAnEntry act,
                CancellationToken cancellationToken) =>
            {
                var entry = await act.ExecuteAsync(id, cancellationToken);

                EntityTags.Write(response, entry.Version);

                return Results.Ok(ScratchpadEntryResponse.Of(entry));
            })
            .WithName("ReadEntry")
            .WithSummary("One entry, and the version a write on it replaces.")
            .Produces<ScratchpadEntryResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPut($"{Entries}/{{id:guid}}", async (
                Guid id,
                RewriteEntryRequest body,
                HttpRequest request,
                HttpResponse response,
                RewriteAnEntry act,
                CancellationToken cancellationToken) =>
            {
                var entry = await act.ExecuteAsync(
                    id, body.Text, body.Pinned, EntityTags.Required(request), cancellationToken);

                EntityTags.Write(response, entry.Version);

                return Results.Ok(ScratchpadEntryResponse.Of(entry));
            })
            .WithName("RewriteEntry")
            .WithSummary("Change an entry's text, its pin, or both.")
            .Produces<ScratchpadEntryResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapDelete($"{Entries}/{{id:guid}}", async (
                Guid id,
                HttpRequest request,
                DiscardAnEntry act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(id, EntityTags.Required(request), cancellationToken);

                return Results.NoContent();
            })
            .WithName("DiscardEntry")
            .WithSummary("Destroy one entry. There is no way back from this.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        return endpoints;
    }
}
