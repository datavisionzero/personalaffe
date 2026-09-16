using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Personalaffe.Application.Acts.Dashboard;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Dashboard;

namespace Personalaffe.Api.Http;

/// <summary>
/// A tile in an address, as the contract spells it: <c>scratchpad</c>,
/// <c>weather</c>.
/// </summary>
/// <remarks>
/// Read as text and turned into a <see cref="DashboardTile"/> here rather than
/// bound as one, for the reason <see cref="Applications"/> gives: the
/// framework's binder would take <c>Weather</c> and refuse <c>weather</c>,
/// which is the one spelling the contract uses.
/// </remarks>
public static class Tiles
{
    public const string Parameter = "tile";

    /// <summary>The word the contract spells this tile.</summary>
    public static string Wire(this DashboardTile tile) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(tile.ToString());

    /// <summary>The tile this word names.</summary>
    /// <exception cref="Domain.Refusal"><c>validation</c>: it names none of them.</exception>
    public static DashboardTile Named(string? word)
    {
        foreach (var tile in Enum.GetValues<DashboardTile>())
        {
            if (string.Equals(tile.Wire(), word, StringComparison.Ordinal))
            {
                return tile;
            }
        }

        throw Domain.Refusal.Validation(
            Parameter,
            "The tiles are " + string.Join(", ", Enum.GetValues<DashboardTile>().Select(Wire)) + ".");
    }

    /// <summary>Gives the document back the closed set the hand-written parsing costs it.</summary>
    public static RouteHandlerBuilder NamesATile(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            foreach (var parameter in operation.Parameters ?? [])
            {
                if (parameter.Name == Parameter && parameter is OpenApiParameter named)
                {
                    named.Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = [.. Enum.GetValues<DashboardTile>().Select(tile => (JsonNode)tile.Wire())],
                    };
                }
            }

            return Task.CompletedTask;
        });
    }
}

/// <summary>One tile of the home page.</summary>
/// <param name="Offered">
/// Whether it can be shown at all: its application is switched on and this
/// caller may read it. A tile that is not offered keeps whatever the owner set,
/// so switching the application back on brings it back as it was.
/// </param>
public sealed record TileResponse(
    DashboardTile Tile, bool Shown, bool Offered, DateTimeOffset UpdatedAt)
{
    public static TileResponse Of(TheTile tile) =>
        new(tile.Tile, tile.Shown, tile.Offered, tile.UpdatedAt);
}

/// <summary>One open task on the home page.</summary>
public sealed record DashboardTaskResponse(
    Guid Id, string Title, Guid ListId, string List, DateOnly? DueOn, DateTimeOffset UpdatedAt)
{
    public static DashboardTaskResponse Of(OpenTask task) =>
        new(task.Id, task.Title, task.ListId, task.List, task.DueOn, task.UpdatedAt);
}

/// <summary>One recently written page.</summary>
public sealed record DashboardPageResponse(
    Guid Id, string Title, Guid? ParentId, DateTimeOffset UpdatedAt)
{
    public static DashboardPageResponse Of(RecentPage page) =>
        new(page.Id, page.Title, page.ParentId, page.UpdatedAt);
}

/// <summary>One recent Scratchpad entry, cut to what a tile draws.</summary>
public sealed record DashboardEntryResponse(
    Guid Id, string Preview, bool Pinned, DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt)
{
    public static DashboardEntryResponse Of(TheLatestEntry entry) =>
        new(entry.Id, entry.Preview, entry.Pinned, entry.UpdatedAt, entry.ExpiresAt);
}

/// <summary>One recently stored file.</summary>
public sealed record DashboardFileResponse(
    Guid Id, string Name, Guid? FolderId, long Size, DateTimeOffset UpdatedAt)
{
    public static DashboardFileResponse Of(RecentFile file) =>
        new(file.Id, file.Name, file.FolderId, file.Size, file.UpdatedAt);
}

/// <summary>
/// The home page: which tiles are on it, and what is in the ones that are.
/// </summary>
/// <remarks>
/// A section is absent where its tile is not being drawn and an empty list
/// where it is drawn and holds nothing. Those are different answers and a
/// screen draws them differently: nothing at all, against "you have no open
/// tasks".
/// </remarks>
public sealed record DashboardResponse(
    IReadOnlyList<TileResponse> Tiles,
    IReadOnlyList<DashboardTaskResponse>? Tasks,
    IReadOnlyList<DashboardPageResponse>? Knowledge,
    IReadOnlyList<DashboardEntryResponse>? Scratchpad,
    IReadOnlyList<DashboardFileResponse>? Files);

/// <summary>Whether a tile is on the home page.</summary>
public sealed record ShowTileRequest(bool Shown);

/// <summary>
/// The home page (<c>docs/api.md</c>, The dashboard): what is useful or
/// pending right now.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One request for the whole page.</strong> It is one screen, it
/// refreshes on a timer, and four requests for four tiles would be four moments
/// at which they could disagree about what this workspace has.
/// </para>
/// <para>
/// <strong>The weather is not in it.</strong> Its tile is in <c>tiles</c> — that
/// is how a client knows to draw it — and what it says is at
/// <c>GET /api/weather</c>. A provider on the other side of the internet must
/// not be able to hold up the owner's home page.
/// </para>
/// </remarks>
public static class DashboardEndpoints
{
    private const string TilesAt = "/dashboard/tiles";

    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/dashboard", async (
                ReadTheDashboard act, CancellationToken cancellationToken) =>
            {
                var dashboard = await act.ExecuteAsync(cancellationToken);

                return Results.Ok(new DashboardResponse(
                    [.. dashboard.Tiles.Select(TileResponse.Of)],
                    dashboard.Tasks?.Select(DashboardTaskResponse.Of).ToArray(),
                    dashboard.Knowledge?.Select(DashboardPageResponse.Of).ToArray(),
                    dashboard.Scratchpad?.Select(DashboardEntryResponse.Of).ToArray(),
                    dashboard.Files?.Select(DashboardFileResponse.Of).ToArray()));
            })
            .WithName("ReadDashboard")
            .WithSummary("What is useful or pending: the tiles, and what is in the ones being drawn.")
            .WithDescription(
                $"Each drawn tile carries at most {ReadTheDashboard.Rows} rows. A section is absent "
                + "where its tile is hidden, out of this caller's reach or in a switched-off "
                + "application; it is an empty list where the tile is drawn and there is nothing in "
                + "it. The weather tile's content is at GET /api/weather.")
            .Produces<DashboardResponse>();

        endpoints.MapPut($"{TilesAt}/{{tile}}", async (
                string tile,
                ShowTileRequest body,
                HttpRequest request,
                HttpResponse response,
                ShowOrHideATile act,
                CancellationToken cancellationToken) =>
            {
                var shown = await act.ExecuteAsync(
                    Tiles.Named(tile), body.Shown, EntityTags.Required(request), cancellationToken);

                EntityTags.Write(response, shown.Version);

                return Results.Ok(TileResponse.Of(shown));
            })
            .WithName("ShowTile")
            .WithSummary("Put one tile on the home page, or take it off. The owner's alone.")
            .WithDescription(
                "A tile that is not offered can still be shown and hidden: the setting is the owner's "
                + "preference and outlives a switched-off application.")
            .Produces<TileResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .NamesATile()
            .Guarded();

        return endpoints;
    }
}
