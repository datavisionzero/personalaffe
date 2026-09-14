using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Tasks end to end, against a real Postgres: the nine addresses, the
/// permission matrix, the guard on every write, the order, and the one value in
/// this product that is a date rather than a moment.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TasksTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Multiple_named_lists_keep_their_tasks_apart()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var einkauf = await board.MakeListAsync("Einkauf", Token);
        var arbeit = await board.MakeListAsync("Arbeit", Token);

        Assert.Equal(HttpStatusCode.Created, einkauf.Status);

        await board.CaptureAsync(einkauf.Id, "Milch holen", Token);
        await board.CaptureAsync(einkauf.Id, "Brot holen", Token);
        await board.CaptureAsync(arbeit.Id, "Bericht schreiben", Token);

        Assert.Equal(["Milch holen", "Brot holen"], await board.OrderAsync(einkauf.Id, Token));
        Assert.Equal(["Bericht schreiben"], await board.OrderAsync(arbeit.Id, Token));

        // The lists come back by name with how much is open in each, because
        // that is the first thing any client draws.
        var lists = (await board.ListsAsync(Token)).Body!["items"]!.AsArray();

        Assert.Equal(["Arbeit", "Einkauf"], lists.Select(list => list!["name"]!.GetValue<string>()));
        Assert.Equal(2, lists.First(list => list!["name"]!.GetValue<string>() == "Einkauf")!["open"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_due_date_is_the_same_calendar_day_it_was_sent_as()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token, dueOn: "2026-09-14");

        // Not an instant, so there is no hour for a timezone to move. This is
        // the whole of how this application has no date bug.
        Assert.Equal("2026-09-14", task.Body!["due_on"]!.GetValue<string>());
        Assert.Equal(
            "2026-09-14",
            (await board.ReadAsync(task.Id, Token)).Body!["due_on"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_task_without_a_due_date_is_a_task_and_one_can_lose_its_date()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Irgendwann", Token);

        Assert.Null(task.Body!["due_on"]?.GetValue<string>());

        var dated = await board.ChangeAsync(task.Id, task.Version, Token, dueOn: "2026-09-20");
        Assert.Equal("2026-09-20", dated.Body!["due_on"]!.GetValue<string>());

        // Sent explicitly as nothing, which is what "no longer due" is.
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/tasks/{task.Id}")
        {
            Content = JsonContent.Create(new
            {
                list = list.Id,
                title = "Irgendwann",
                description = string.Empty,
                due_on = (string?)null,
                completed = false,
                after = (Guid?)null,
            }),
        };

        request.Headers.TryAddWithoutValidation("If-Match", dated.Version);

        using var response = await board.Owner.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await board.ReadAsync(task.Id, Token)).Body!["due_on"]?.GetValue<string>());
    }

    [Fact]
    public async Task Completing_records_when_and_reopening_forgets_it()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        var done = await board.ChangeAsync(task.Id, task.Version, Token, completed: true);

        Assert.True(done.Body!["completed"]!.GetValue<bool>());
        Assert.NotNull(done.Body["completed_at"]);

        var again = await board.ChangeAsync(task.Id, done.Version, Token, completed: false);

        Assert.False(again.Body!["completed"]!.GetValue<bool>());
        Assert.Null(again.Body["completed_at"]?.GetValue<DateTimeOffset?>());

        // Open and completed come back in one order: separating them is what a
        // client draws, and doing it here would mean a caller could not put a
        // task back where it was after reopening it.
        Assert.Equal(1, (await board.ListsAsync(Token)).Body!["items"]!.AsArray()
            .Single()!["open"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_task_goes_where_the_caller_says_and_nothing_else_moves()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);

        var one = await board.CaptureAsync(list.Id, "eins", Token);
        var two = await board.CaptureAsync(list.Id, "zwei", Token);
        var three = await board.CaptureAsync(list.Id, "drei", Token);

        Assert.Equal(["eins", "zwei", "drei"], await board.OrderAsync(list.Id, Token));

        // To the top.
        await board.ChangeAsync(three.Id, three.Version, Token, toTheTop: true);
        Assert.Equal(["drei", "eins", "zwei"], await board.OrderAsync(list.Id, Token));

        // And behind a named neighbour.
        var moved = (await board.ReadAsync(three.Id, Token)).Version;
        await board.ChangeAsync(three.Id, moved, Token, after: one.Id);
        Assert.Equal(["eins", "drei", "zwei"], await board.OrderAsync(list.Id, Token));

        // Nothing else moved: the two that stayed put still carry the versions
        // they were read at, so nobody else's write has gone stale.
        Assert.Equal(HttpStatusCode.OK, (await board.ChangeAsync(one.Id, one.Version, Token)).Status);
        Assert.Equal(HttpStatusCode.OK, (await board.ChangeAsync(two.Id, two.Version, Token)).Status);
    }

    [Fact]
    public async Task A_task_can_only_be_put_behind_one_in_the_same_list()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var einkauf = await board.MakeListAsync("Einkauf", Token);
        var arbeit = await board.MakeListAsync("Arbeit", Token);

        var here = await board.CaptureAsync(einkauf.Id, "Milch holen", Token);
        var elsewhere = await board.CaptureAsync(arbeit.Id, "Bericht schreiben", Token);

        var refused = await board.ChangeAsync(here.Id, here.Version, Token, after: elsewhere.Id);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("validation", refused.Code);
    }

    [Fact]
    public async Task A_task_moved_to_another_list_lands_where_it_was_put()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var einkauf = await board.MakeListAsync("Einkauf", Token);
        var arbeit = await board.MakeListAsync("Arbeit", Token);

        var task = await board.CaptureAsync(einkauf.Id, "Milch holen", Token);
        await board.CaptureAsync(arbeit.Id, "Bericht schreiben", Token);

        var moved = await board.ChangeAsync(
            task.Id, task.Version, Token, list: arbeit.Id, toTheTop: true);

        Assert.Equal(HttpStatusCode.OK, moved.Status);
        Assert.Equal(arbeit.Id, moved.Body!["list"]!.GetValue<Guid>());
        Assert.Equal(["Milch holen", "Bericht schreiben"], await board.OrderAsync(arbeit.Id, Token));
        Assert.Empty(await board.OrderAsync(einkauf.Id, Token));
    }

    [Fact]
    public async Task Fifty_moves_into_the_same_place_keep_the_order_and_renumber_when_they_must()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);

        var first = await board.CaptureAsync(list.Id, "eins", Token);
        await board.CaptureAsync(list.Id, "zwei", Token);

        // Every one of these lands between the same two neighbours, which is
        // what runs the midpoints out. The order has to survive it.
        for (var each = 0; each < 60; each++)
        {
            var task = await board.CaptureAsync(list.Id, $"dazwischen {each}", Token);
            var moved = await board.ChangeAsync(task.Id, task.Version, Token, after: first.Id);

            Assert.Equal(HttpStatusCode.OK, moved.Status);
        }

        var order = await board.OrderAsync(list.Id, Token);

        Assert.Equal(62, order.Count);
        Assert.Equal("eins", order[0]);
        Assert.Equal("dazwischen 59", order[1]);
        Assert.Equal("zwei", order[^1]);
    }

    [Fact]
    public async Task Two_changes_from_one_read_and_the_second_changes_nothing()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        Assert.Equal(
            HttpStatusCode.OK,
            (await board.ChangeAsync(task.Id, task.Version, Token, title: "Milch und Brot")).Status);

        // The same version again: a content change, a completion and a move are
        // all one write and all guarded the same way.
        foreach (var stale in new[]
                 {
                     await board.ChangeAsync(task.Id, task.Version, Token, title: "etwas anderes"),
                     await board.ChangeAsync(task.Id, task.Version, Token, completed: true),
                     await board.ChangeAsync(task.Id, task.Version, Token, toTheTop: true),
                 })
        {
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
            Assert.Equal("stale", stale.Code);
        }

        var read = await board.ReadAsync(task.Id, Token);

        Assert.Equal("Milch und Brot", read.Body!["title"]!.GetValue<string>());
        Assert.False(read.Body["completed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_list_name_is_one_each_whatever_its_capitals()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        await board.MakeListAsync("Einkauf", Token);

        var refused = await board.MakeListAsync("einkauf", Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("conflict", refused.Code);

        var other = await board.MakeListAsync("Arbeit", Token);
        var renamed = await board.RenameListAsync(other.Id, "EINKAUF", other.Version, Token);

        Assert.Equal(HttpStatusCode.Conflict, renamed.Status);
    }

    [Fact]
    public async Task A_deleted_list_takes_its_tasks_and_comes_back_with_them()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await board.DiscardListAsync(list.Id, list.Version, Token)).Status);

        Assert.Empty((await board.ListsAsync(Token)).Body!["items"]!.AsArray());
        Assert.Equal("deleted", (await board.ReadAsync(task.Id, Token)).Code);

        // One entry: the list the owner deleted.
        var items = (await board.TrashAsync(Token))["items"]!.AsArray();

        Assert.Single(items);
        Assert.Equal("Einkauf", items[0]!["name"]!.GetValue<string>());

        Assert.Equal(
            HttpStatusCode.OK, (await board.RestoreAsync(list.Id, Versions.Of(items[0]!), Token)).Status);

        Assert.Equal(["Milch holen"], await board.OrderAsync(list.Id, Token));
    }

    [Fact]
    public async Task A_task_deleted_on_its_own_comes_back_at_the_end_of_its_list()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var going = await board.CaptureAsync(list.Id, "Milch holen", Token);
        await board.CaptureAsync(list.Id, "Brot holen", Token);

        await board.DiscardAsync(going.Id, going.Version, Token);

        Assert.Equal(["Brot holen"], await board.OrderAsync(list.Id, Token));

        var entry = (await board.TrashAsync(Token))["items"]!.AsArray().Single()!;

        Assert.Equal("Einkauf", entry["where"]!.GetValue<string>());

        var restored = await board.RestoreAsync(going.Id, Versions.Of(entry), Token);

        Assert.Equal(HttpStatusCode.OK, restored.Status);

        // At the end, because where it used to sit is a number the list may have
        // reused, and the end is the one place that is always free.
        Assert.Equal(["Brot holen", "Milch holen"], await board.OrderAsync(list.Id, Token));
    }

    [Fact]
    public async Task The_sweep_takes_a_list_and_its_tasks_together()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        await board.CaptureAsync(list.Id, "Milch holen", Token);

        await board.DiscardListAsync(list.Id, list.Version, Token);
        await board.BackdateAsync(TimeSpan.FromDays(31), Token);

        var swept = await board.PurgeAsync(Token);

        Assert.True(swept.Swept);
        Assert.Equal(2, swept.Total);
        Assert.Empty((await board.TrashAsync(Token))["items"]!.AsArray());
    }

    [Fact]
    public async Task An_agent_that_may_read_tasks_may_not_change_them()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        using var reader = await board.AnAgentReachingAsync("read", Token);

        Assert.Equal(HttpStatusCode.OK, (await board.ListsAsync(Token, reader)).Status);
        Assert.Equal(HttpStatusCode.OK, (await board.TasksAsync(list.Id, Token, reader)).Status);
        Assert.Equal(HttpStatusCode.OK, (await board.ReadAsync(task.Id, Token, reader)).Status);

        Assert.Equal(HttpStatusCode.Forbidden, (await board.MakeListAsync("Arbeit", Token, reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await board.CaptureAsync(list.Id, "noch etwas", Token, asWhom: reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await board.ChangeAsync(task.Id, task.Version, Token, completed: true, asWhom: reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await board.DiscardAsync(task.Id, task.Version, Token, reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await board.DiscardListAsync(list.Id, list.Version, Token, reader)).Status);
    }

    [Fact]
    public async Task An_agent_without_tasks_cannot_reach_one_of_them()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        using var stranger = await board.AnAgentReachingAsync("none", Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await board.ListsAsync(Token, stranger)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await board.TasksAsync(list.Id, Token, stranger)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await board.ReadAsync(task.Id, Token, stranger)).Status);
    }

    [Fact]
    public async Task A_switched_off_tasks_refuses_every_address_and_keeps_everything()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        await board.SwitchAsync(enabled: false, Token);

        Assert.Equal("disabled", (await board.ListsAsync(Token)).Code);
        Assert.Equal("disabled", (await board.TasksAsync(list.Id, Token)).Code);
        Assert.Equal("disabled", (await board.ReadAsync(task.Id, Token)).Code);
        Assert.Equal("disabled", (await board.MakeListAsync("Arbeit", Token)).Code);

        await board.SwitchAsync(enabled: true, Token);

        Assert.Equal(["Milch holen"], await board.OrderAsync(list.Id, Token));
    }

    [Fact]
    public async Task A_field_a_request_does_not_define_is_said_out_loud()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        using var refused = await board.Owner.PostAsJsonAsync(
            "/api/tasks/lists", new { name = "Einkauf", colour = "blau" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var body = JsonNode.Parse(await refused.Content.ReadAsStringAsync(Token))!;
        Assert.Equal("/problems/unknown-field", body["type"]!.GetValue<string>());
    }
}
