using Personalaffe.Application.Acts.Tasks;

namespace Personalaffe.Api.Http;

/// <summary>One task list, and how much is in it.</summary>
public sealed record TaskListResponse(
    Guid Id, string Name, int Open, int All, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static TaskListResponse Of(TheTaskList list) => new(
        list.Id, list.Name, list.Open, list.All, list.CreatedAt, list.UpdatedAt);
}

/// <summary>The lists.</summary>
public sealed record TaskListsResponse(IReadOnlyList<TaskListResponse> Items);

/// <summary>One task.</summary>
/// <param name="DueOn">
/// The day it is due, as <c>2026-09-14</c>, or nothing. <strong>A date and
/// never a moment</strong>: the fourteenth is the fourteenth wherever the owner
/// is standing.
/// </param>
/// <param name="After">
/// The task it sits behind in its list, or nothing when it is first. A
/// neighbour and not a number, because a number is the module's arithmetic and
/// a neighbour is what a caller can act on.
/// </param>
public sealed record TaskResponse(
    Guid Id,
    Guid List,
    string Title,
    string Description,
    DateOnly? DueOn,
    bool Completed,
    DateTimeOffset? CompletedAt,
    Guid? After,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static TaskResponse Of(TheTask task) => new(
        task.Id,
        task.List,
        task.Title,
        task.Description,
        task.DueOn,
        task.Completed,
        task.CompletedAt,
        task.After,
        task.CreatedAt,
        task.UpdatedAt);
}

/// <summary>What is in a list, in the owner's order.</summary>
public sealed record TasksResponse(IReadOnlyList<TaskResponse> Items);

/// <summary>A list to make, or a new name for one.</summary>
public sealed record TaskListRequest(string? Name);

/// <summary>A task to capture: it goes at the end of the list.</summary>
public sealed record CaptureTaskRequest(string? Title, string? Description, DateOnly? DueOn);

/// <summary>
/// What a task is being changed to — all of it, because all of it is one row.
/// </summary>
/// <param name="After">
/// The task it goes behind, or nothing for the top of its list. A caller that
/// is not moving anything sends the neighbour it already has.
/// </param>
public sealed record ChangeTaskRequest(
    Guid List,
    string? Title,
    string? Description,
    DateOnly? DueOn,
    bool Completed,
    Guid? After);

/// <summary>
/// Tasks (<c>docs/api.md</c>, Tasks): personal commitments in named lists, in
/// an order the owner sets.
/// </summary>
/// <remarks>
/// <para>
/// Nine addresses, and every act behind them opens with
/// <c>ReachingAnApplication</c>: access first, then the switch.
/// </para>
/// <para>
/// <strong>One <c>PUT</c> carries everything a task is</strong> — the title,
/// the description, the due date, the list, whether it is done and where it
/// sits. That is the shape the Scratchpad established and the other two kept,
/// and a task is exactly where a second address per field would start to look
/// reasonable and be wrong.
/// </para>
/// </remarks>
public static class TaskEndpoints
{
    private const string Lists = "/tasks/lists";

    private const string OneTask = "/tasks";

    public static IEndpointRouteBuilder MapTasks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Lists, async (ReadTheLists act, CancellationToken cancellationToken) =>
            {
                var lists = await act.ExecuteAsync(cancellationToken);

                return Results.Ok(new TaskListsResponse([.. lists.Select(TaskListResponse.Of)]));
            })
            .WithName("ReadLists")
            .WithSummary("The lists, by name, with how much is open in each.")
            .Produces<TaskListsResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost(Lists, async (
                TaskListRequest body,
                HttpResponse response,
                MakeAList act,
                CancellationToken cancellationToken) =>
            {
                var list = await act.ExecuteAsync(body.Name, cancellationToken);

                EntityTags.Write(response, list.Version);

                return Results.Created($"{Routes.Api}{Lists}/{list.Id}/tasks", TaskListResponse.Of(list));
            })
            .WithName("MakeList")
            .WithSummary("Make a list.")
            .Produces<TaskListResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPut($"{Lists}/{{id:guid}}", async (
                Guid id,
                TaskListRequest body,
                HttpRequest request,
                HttpResponse response,
                RenameAList act,
                CancellationToken cancellationToken) =>
            {
                var list = await act.ExecuteAsync(
                    id, body.Name, EntityTags.Required(request), cancellationToken);

                EntityTags.Write(response, list.Version);

                return Results.Ok(TaskListResponse.Of(list));
            })
            .WithName("RenameList")
            .WithSummary("Rename a list.")
            .Produces<TaskListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapDelete($"{Lists}/{{id:guid}}", async (
                Guid id,
                HttpRequest request,
                DiscardAList act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(id, EntityTags.Required(request), cancellationToken);

                return Results.NoContent();
            })
            .WithName("DiscardList")
            .WithSummary("Put a list in the Trash, with every task in it.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapGet($"{Lists}/{{id:guid}}/tasks", async (
                Guid id,
                ReadTheTasks act,
                CancellationToken cancellationToken) =>
            {
                var tasks = await act.ExecuteAsync(id, cancellationToken);

                return Results.Ok(new TasksResponse([.. tasks.Select(TaskResponse.Of)]));
            })
            .WithName("ReadTasks")
            .WithSummary("What is in a list, open and completed together, in the owner's order.")
            .Produces<TasksResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost($"{Lists}/{{id:guid}}/tasks", async (
                Guid id,
                CaptureTaskRequest body,
                HttpResponse response,
                CaptureATask act,
                CancellationToken cancellationToken) =>
            {
                var task = await act.ExecuteAsync(
                    id, body.Title, body.Description, body.DueOn, cancellationToken);

                EntityTags.Write(response, task.Version);

                return Results.Created($"{Routes.Api}{OneTask}/{task.Id}", TaskResponse.Of(task));
            })
            .WithName("CaptureTask")
            .WithSummary("Capture a task at the end of a list.")
            .Produces<TaskResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet($"{OneTask}/{{id:guid}}", async (
                Guid id,
                HttpResponse response,
                ReadATask act,
                CancellationToken cancellationToken) =>
            {
                var task = await act.ExecuteAsync(id, cancellationToken);

                EntityTags.Write(response, task.Version);

                return Results.Ok(TaskResponse.Of(task));
            })
            .WithName("ReadTask")
            .WithSummary("One task, and the version a write on it replaces.")
            .Produces<TaskResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPut($"{OneTask}/{{id:guid}}", async (
                Guid id,
                ChangeTaskRequest body,
                HttpRequest request,
                HttpResponse response,
                ChangeATask act,
                CancellationToken cancellationToken) =>
            {
                var task = await act.ExecuteAsync(
                    id,
                    body.List,
                    body.Title,
                    body.Description,
                    body.DueOn,
                    body.Completed,
                    body.After,
                    EntityTags.Required(request),
                    cancellationToken);

                EntityTags.Write(response, task.Version);

                return Results.Ok(TaskResponse.Of(task));
            })
            .WithName("ChangeTask")
            .WithSummary("Everything about a task: its title, its date, its list, its state, its place.")
            .Produces<TaskResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        endpoints.MapDelete($"{OneTask}/{{id:guid}}", async (
                Guid id,
                HttpRequest request,
                DiscardATask act,
                CancellationToken cancellationToken) =>
            {
                await act.ExecuteAsync(id, EntityTags.Required(request), cancellationToken);

                return Results.NoContent();
            })
            .WithName("DiscardTask")
            .WithSummary("Put a task in the Trash. It comes back at the end of its list.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Guarded();

        return endpoints;
    }
}
