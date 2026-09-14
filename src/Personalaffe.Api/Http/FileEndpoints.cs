using Personalaffe.Application.Acts.Files;

namespace Personalaffe.Api.Http;

/// <summary>One stored file.</summary>
/// <param name="Folder">The folder it is in, or nothing for one at the top.</param>
/// <param name="MediaType">
/// What the caller said the bytes are when they were stored. The instance never
/// guesses it from the name and never asserts it about the bytes.
/// </param>
public sealed record FileResponse(
    Guid Id,
    string Name,
    Guid? Folder,
    long Size,
    string MediaType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static FileResponse Of(TheFile file) => new(
        file.Id, file.Name, file.Folder, file.Size, file.MediaType, file.CreatedAt, file.UpdatedAt);
}

/// <summary>One folder.</summary>
public sealed record FolderResponse(
    Guid Id, string Name, Guid? Parent, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static FolderResponse Of(TheFolder folder) => new(
        folder.Id, folder.Name, folder.Parent, folder.CreatedAt, folder.UpdatedAt);
}

/// <summary>What is in one folder, where it is, and how much room is left.</summary>
/// <param name="Chain">
/// The folders from the top down to this one — the breadcrumb. Empty at the
/// top.
/// </param>
public sealed record FilesResponse(
    IReadOnlyList<FolderResponse> Chain,
    IReadOnlyList<FolderResponse> Folders,
    IReadOnlyList<FileResponse> Files,
    long UsedBytes,
    long MaxFileBytes,
    long MaxTotalBytes);

/// <summary>A folder to make: what it is called, and where.</summary>
public sealed record MakeFolderRequest(string? Name, Guid? Parent);

/// <summary>
/// What something is being changed to — the name and the place together.
/// </summary>
/// <remarks>
/// One shape for both, because a rename and a move are both changes to the same
/// row. A second address carrying one of them would be a second place the guard
/// has to be got right, and "move it there and call it something else" would be
/// two writes where the owner made one decision.
/// </remarks>
public sealed record ChangeRequest(string? Name, Guid? Folder);

/// <summary>
/// Files (<c>docs/api.md</c>, Files): the owner's own storage, with folders,
/// a Trash and a reference a rename cannot break.
/// </summary>
/// <remarks>
/// <para>
/// Ten addresses, and every act behind them opens with
/// <c>ReachingAnApplication</c>: access first, then the switch. The two that
/// carry bytes — the upload and the download — go through exactly the same
/// door as the eight that carry JSON, because VISION §8 asks that file
/// retrievals require authentication and a download outside the door would be
/// the one hole in that.
/// </para>
/// <para>
/// <strong>A download is always an attachment.</strong> Whatever a file's
/// stored media type says, it is served with
/// <c>Content-Disposition: attachment</c> and <c>X-Content-Type-Options:
/// nosniff</c>, so that a stored HTML page is a file the browser saves rather
/// than a document on this instance's own origin with the owner's session
/// attached to it. The MVP has no previews (VISION §11), so nothing is lost by
/// it.
/// </para>
/// </remarks>
public static class FileEndpoints
{
    private const string Files = "/files";

    private const string Folders = $"{Files}/folders";

    public static IEndpointRouteBuilder MapFiles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Files, async (
                Guid? folder,
                ReadTheFolder act,
                CancellationToken cancellationToken) =>
            {
                var listing = await act.ExecuteAsync(folder, cancellationToken);

                return Results.Ok(new FilesResponse(
                    [.. listing.Chain.Select(FolderResponse.Of)],
                    [.. listing.Folders.Select(FolderResponse.Of)],
                    [.. listing.Files.Select(FileResponse.Of)],
                    listing.UsedBytes,
                    listing.MaxFileBytes,
                    listing.MaxTotalBytes));
            })
            .WithName("ReadFolder")
            .WithSummary("What is in a folder, where it is, and how much room is left.")
            .Produces<FilesResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost(Folders, async (
                MakeFolderRequest body,
                HttpResponse response,
                MakeAFolder act,
                CancellationToken cancellationToken) =>
            {
                var folder = await act.ExecuteAsync(body.Name, body.Parent, cancellationToken);

                EntityTags.Write(response, folder.Version);

                return Results.Created(
                    $"{Routes.Api}{Files}?folder={folder.Id}", FolderResponse.Of(folder));
            })
            .WithName("MakeFolder")
            .WithSummary("Make a folder.")
            .Produces<FolderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPut($"{Folders}/{{id:guid}}", async (
                Guid id,
                ChangeRequest body,
                HttpRequest request,
                HttpResponse response,
                MoveOrRenameAFolder act,
                CancellationToken cancellationToken) =>
            {
                var folder = await act.ExecuteAsync(
                    id, body.Name, body.Folder, EntityTags.Required(request), cancellationToken);

                EntityTags.Write(response, folder.Version);

                return Results.Ok(FolderResponse.Of(folder));
            })
            .WithName("ChangeFolder")
            .WithSummary("Rename a folder, move it, or both. What is in it goes with it.")
            .Produces<FolderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapDelete($"{Folders}/{{id:guid}}", async (
                Guid id,
                HttpRequest request,
                DiscardAFolder act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(id, EntityTags.Required(request), cancellationToken);

                return Results.NoContent();
            })
            .WithName("DiscardFolder")
            .WithSummary("Put a folder in the Trash, with everything in it.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapPost($"{Files}/content", async (
                string name,
                Guid? folder,
                HttpRequest request,
                HttpResponse response,
                UploadAFile act,
                CancellationToken cancellationToken) =>
            {
                var file = await act.ExecuteAsync(
                    name, folder, request.ContentType, request.Body, cancellationToken);

                EntityTags.Write(response, file.Version);

                return Results.Created($"{Routes.Api}{Files}/{file.Id}", FileResponse.Of(file));
            })
            .WithName("UploadFile")
            .WithSummary("Store a file. The body is the file; the name is a parameter.")
            .Produces<FileResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status507InsufficientStorage)
            .TakesBytes();

        endpoints.MapGet($"{Files}/{{id:guid}}", async (
                Guid id,
                HttpResponse response,
                ReadAFile act,
                CancellationToken cancellationToken) =>
            {
                var file = await act.ExecuteAsync(id, cancellationToken);

                EntityTags.Write(response, file.Version);

                return Results.Ok(FileResponse.Of(file));
            })
            .WithName("ReadFile")
            .WithSummary("One file's metadata, and the version a write on it replaces.")
            .Produces<FileResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPut($"{Files}/{{id:guid}}", async (
                Guid id,
                ChangeRequest body,
                HttpRequest request,
                HttpResponse response,
                MoveOrRenameAFile act,
                CancellationToken cancellationToken) =>
            {
                var file = await act.ExecuteAsync(
                    id, body.Name, body.Folder, EntityTags.Required(request), cancellationToken);

                EntityTags.Write(response, file.Version);

                return Results.Ok(FileResponse.Of(file));
            })
            .WithName("ChangeFile")
            .WithSummary("Rename a file, move it, or both. Its address does not change.")
            .Produces<FileResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapPut($"{Files}/{{id:guid}}/content", async (
                Guid id,
                HttpRequest request,
                HttpResponse response,
                ReplaceTheBytes act,
                CancellationToken cancellationToken) =>
            {
                var file = await act.ExecuteAsync(
                    id,
                    request.ContentType,
                    request.Body,
                    EntityTags.Required(request),
                    cancellationToken);

                EntityTags.Write(response, file.Version);

                return Results.Ok(FileResponse.Of(file));
            })
            .WithName("ReplaceFileContent")
            .WithSummary("New bytes for the same file. Every link to it now answers with these.")
            .Produces<FileResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status507InsufficientStorage)
            .TakesBytes()
            .Guarded();

        endpoints.MapGet($"{Files}/{{id:guid}}/content", async (
                Guid id,
                HttpResponse response,
                DownloadAFile act,
                CancellationToken cancellationToken) =>
            {
                var download = await act.ExecuteAsync(id, cancellationToken);

                EntityTags.Write(response, download.File.Version);

                // Whatever the file says it is, the browser is told not to work
                // it out for itself. A stored `.html` served as a document of
                // this instance's own origin would be script running with the
                // owner's session.
                response.Headers.XContentTypeOptions = "nosniff";

                // `Results.Stream` writes `Content-Disposition: attachment` with
                // the name spelled both ways — plain for the clients that read
                // it and `filename*=UTF-8''…` for a name that is not ASCII — and
                // disposes the stream when it is done with it.
                return Results.Stream(
                    download.Content, download.File.MediaType, download.File.Name);
            })
            .WithName("DownloadFile")
            .WithSummary("A file's bytes. The address is the id, so a rename cannot break it.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AnswersBytes();

        endpoints.MapDelete($"{Files}/{{id:guid}}", async (
                Guid id,
                HttpRequest request,
                DiscardAFile act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(id, EntityTags.Required(request), cancellationToken);

                return Results.NoContent();
            })
            .WithName("DiscardFile")
            .WithSummary("Put a file in the Trash. It can be restored until its retention runs out.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        return endpoints;
    }
}
