using Personalaffe.Application.Acts.Bookmarks;
using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

public sealed record BookmarkResponse(Guid Id, string Title, string Url, string Description, Guid? Folder,
    bool Private, bool Favorite, double? FavoritePosition, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static BookmarkResponse Of(SavedBookmark saved)
    {
        var row = saved.Content;
        return new(row.Id, row.Title, row.Url, row.Description, row.FolderId, saved.Private,
            row.FavoritePosition.HasValue, row.FavoritePosition, row.CreatedAt, row.UpdatedAt);
    }
}
public sealed record BookmarkFolderResponse(Guid Id, string Name, Guid? Parent, bool Private, bool EffectivePrivate,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static BookmarkFolderResponse Of(SavedBookmarkFolder saved)
    {
        var row = saved.Content;
        return new(row.Id, row.Name, row.ParentId, row.Private, saved.EffectivePrivate, row.CreatedAt, row.UpdatedAt);
    }
}
public sealed record BookmarksResponse(IReadOnlyList<BookmarkResponse> Items, int? NextOffset);
public sealed record BookmarkFoldersResponse(IReadOnlyList<BookmarkFolderResponse> Items, int? NextOffset);
public sealed record BookmarkRequest(string? Title, string? Url, string? Description, Guid? Folder);
public sealed record BookmarkFolderRequest(string? Name, Guid? Parent, bool Private);

public sealed record FavoriteBookmarkRequest(bool Favorite, Guid? After);
public sealed record OpenBookmarkRequest(Guid EventId);
public sealed record BookmarkDashboardResponse(IReadOnlyList<BookmarkResponse> Favorites,
    IReadOnlyList<BookmarkResponse> Frequent, IReadOnlyList<BookmarkResponse> Recent, bool HasMoreFavorites);

public static class BookmarkEndpoints
{
    public static IEndpointRouteBuilder MapBookmarks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bookmarks", async (int? offset, int? limit, string? q, Guid? folder, bool? favorites, bool? unsorted, string? sort, BookmarkActs act, CancellationToken token) =>
        {
            var rows = await act.ListAsync(offset, limit, token, q, folder, favorites, unsorted, sort);
            return Results.Ok(new BookmarksResponse([.. rows.Items.Select(BookmarkResponse.Of)], rows.NextOffset));
        }).WithName("ListBookmarks").BookmarkErrors().Produces<BookmarksResponse>();
        endpoints.MapGet("/bookmarks/folders", async (int? offset, int? limit, BookmarkActs act, CancellationToken token) =>
        {
            var rows = await act.FoldersAsync(offset, limit, token);
            return Results.Ok(new BookmarkFoldersResponse([.. rows.Items.Select(BookmarkFolderResponse.Of)], rows.NextOffset));
        }).WithName("ListBookmarkFolders").BookmarkErrors().Produces<BookmarkFoldersResponse>();

        endpoints.MapGet("/bookmarks/{id:guid}", async (Guid id, HttpResponse response, BookmarkActs act, CancellationToken token) =>
        {
            var row = await act.ReadAsync(id, token);
            EntityTags.Write(response, row.Content.Version);
            return Results.Ok(BookmarkResponse.Of(row));
        }).WithName("ReadBookmark").BookmarkErrors().Produces<BookmarkResponse>();
        endpoints.MapPost("/bookmarks", async (BookmarkRequest body, HttpResponse response, BookmarkActs act, CancellationToken token) =>
        {
            var row = await act.CreateAsync(body.Title, body.Url, body.Description, body.Folder, token);
            EntityTags.Write(response, row.Content.Version);
            return Results.Created($"/api/bookmarks/{row.Content.Id}", BookmarkResponse.Of(row));
        }).WithName("CreateBookmark").BookmarkErrors().Produces<BookmarkResponse>(StatusCodes.Status201Created);
        endpoints.MapPut("/bookmarks/{id:guid}", async (Guid id, BookmarkRequest body, HttpRequest request, HttpResponse response, BookmarkActs act, CancellationToken token) =>
        {
            var row = await act.ChangeAsync(id, body.Title, body.Url, body.Description, body.Folder, EntityTags.Required(request), token);
            EntityTags.Write(response, row.Content.Version);
            return Results.Ok(BookmarkResponse.Of(row));
        }).WithName("ChangeBookmark").BookmarkErrors().Produces<BookmarkResponse>().Guarded();
        endpoints.MapDelete("/bookmarks/{id:guid}", async (Guid id, HttpRequest request, BookmarkActs act, CancellationToken token) =>
        {
            await act.DeleteAsync(id, EntityTags.Required(request), token);
            return Results.NoContent();
        }).WithName("DeleteBookmark").BookmarkErrors().Produces(StatusCodes.Status204NoContent).Guarded();

        endpoints.MapGet("/bookmarks/folders/{id:guid}", async (Guid id, HttpResponse response, BookmarkActs act, CancellationToken token) =>
        {
            var row = await act.ReadFolderAsync(id, token);
            EntityTags.Write(response, row.Content.Version);
            return Results.Ok(BookmarkFolderResponse.Of(row));
        }).WithName("ReadBookmarkFolder").BookmarkErrors().Produces<BookmarkFolderResponse>();
        endpoints.MapPost("/bookmarks/folders", async (BookmarkFolderRequest body, HttpResponse response, BookmarkActs act, CancellationToken token) =>
        {
            var row = await act.CreateFolderAsync(body.Name, body.Parent, body.Private, token);
            EntityTags.Write(response, row.Content.Version);
            return Results.Created($"/api/bookmarks/folders/{row.Content.Id}", BookmarkFolderResponse.Of(row));
        }).WithName("CreateBookmarkFolder").BookmarkErrors().Produces<BookmarkFolderResponse>(StatusCodes.Status201Created);
        endpoints.MapPut("/bookmarks/folders/{id:guid}", async (Guid id, BookmarkFolderRequest body, HttpRequest request, HttpResponse response, BookmarkActs act, CancellationToken token) =>
        {
            var row = await act.ChangeFolderAsync(id, body.Name, body.Parent, body.Private, EntityTags.Required(request), token);
            EntityTags.Write(response, row.Content.Version);
            return Results.Ok(BookmarkFolderResponse.Of(row));
        }).WithName("ChangeBookmarkFolder").BookmarkErrors().Produces<BookmarkFolderResponse>().Guarded();
        endpoints.MapDelete("/bookmarks/folders/{id:guid}", async (Guid id, HttpRequest request, BookmarkActs act, CancellationToken token) =>
        {
            await act.DeleteFolderAsync(id, EntityTags.Required(request), token);
            return Results.NoContent();
        }).WithName("DeleteBookmarkFolder").BookmarkErrors().Produces(StatusCodes.Status204NoContent).Guarded();
        endpoints.MapPut("/bookmarks/{id:guid}/favorite", async (Guid id, FavoriteBookmarkRequest body,
            HttpRequest request, HttpResponse response, BookmarkActs act, CancellationToken token) =>
        {
            var row = await act.FavoriteAsync(id, body.Favorite, body.After, EntityTags.Required(request), token);
            EntityTags.Write(response, row.Content.Version);
            return Results.Ok(BookmarkResponse.Of(row));
        }).WithName("FavoriteBookmark").BookmarkErrors().Produces<BookmarkResponse>().Guarded();
        endpoints.MapPost("/bookmarks/{id:guid}/open", async (Guid id, OpenBookmarkRequest body, BookmarkActs act, CancellationToken token) =>
        {
            await act.OpenAsync(id, body.EventId, token);
            return Results.NoContent();
        }).WithName("RecordBookmarkOpening").BookmarkErrors().Produces(StatusCodes.Status204NoContent);
        endpoints.MapGet("/bookmarks/dashboard", async (int? limit, BookmarkActs act, CancellationToken token) =>
        {
            var rows = await act.DashboardAsync(limit, token);
            return Results.Ok(new BookmarkDashboardResponse([.. rows.Favorites.Select(BookmarkResponse.Of)],
                [.. rows.Frequent.Select(BookmarkResponse.Of)], [.. rows.Recent.Select(BookmarkResponse.Of)], rows.HasMoreFavorites));
        }).WithName("ReadBookmarkDashboard").BookmarkErrors().Produces<BookmarkDashboardResponse>();
        return endpoints;
    }
    private static RouteHandlerBuilder BookmarkErrors(this RouteHandlerBuilder route) => route
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

}
