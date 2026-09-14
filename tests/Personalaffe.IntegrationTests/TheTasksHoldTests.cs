using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// PERSONAL-E8's promises, one test each, end to end against a real Postgres —
/// the shape the four epics before it gave theirs.
/// </summary>
/// <remarks>
/// The surfaces are covered in detail elsewhere: <see cref="TasksTests"/> is
/// the API, <c>src/cli/internal/cmd/tasks_test.go</c> the CLI and
/// <c>src/web/browser/tasks.spec.ts</c> the browser. What is here is the epic's
/// own list, so that a promise nobody can find a test for is a promise that is
/// not kept.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheTasksHoldTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Multiple_named_lists_keep_their_tasks_apart_and_in_the_owners_order()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var einkauf = await board.MakeListAsync("Einkauf", Token);
        var arbeit = await board.MakeListAsync("Arbeit", Token);

        foreach (var title in (string[])["eins", "zwei", "drei"])
        {
            await board.CaptureAsync(einkauf.Id, title, Token);
        }

        await board.CaptureAsync(arbeit.Id, "etwas anderes", Token);

        Assert.Equal(["eins", "zwei", "drei"], await board.OrderAsync(einkauf.Id, Token));
        Assert.Equal(["etwas anderes"], await board.OrderAsync(arbeit.Id, Token));

        var last = (await board.TasksAsync(einkauf.Id, Token)).Body!["items"]!.AsArray()[^1]!;

        await board.ChangeAsync(last["id"]!.GetValue<Guid>(), Versions.Of(last), Token, toTheTop: true);

        Assert.Equal(["drei", "eins", "zwei"], await board.OrderAsync(einkauf.Id, Token));
        Assert.Equal(["etwas anderes"], await board.OrderAsync(arbeit.Id, Token));
    }

    [Fact]
    public async Task A_human_and_an_agent_do_the_same_ordinary_things()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        using var agent = await board.AnAgentReachingAsync("read_write", Token);

        var list = await board.MakeListAsync("Einkauf", Token, agent);
        Assert.Equal(HttpStatusCode.Created, list.Status);

        var task = await board.CaptureAsync(list.Id, "Milch holen", Token, asWhom: agent);
        Assert.Equal(HttpStatusCode.Created, task.Status);

        // What the agent captured, the owner completes; what the owner
        // completes, the agent reopens. Neither is a second class of caller.
        var done = await board.ChangeAsync(task.Id, task.Version, Token, completed: true);
        Assert.True(done.Body!["completed"]!.GetValue<bool>());

        var again = await board.ChangeAsync(task.Id, done.Version, Token, completed: false, asWhom: agent);
        Assert.False(again.Body!["completed"]!.GetValue<bool>());

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await board.DiscardAsync(task.Id, again.Version, Token, agent)).Status);
    }

    [Fact]
    public async Task A_due_date_is_the_same_calendar_day_whatever_timezone_reads_it()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token, dueOn: "2026-09-14");

        Assert.Equal("2026-09-14", task.Body!["due_on"]!.GetValue<string>());

        // The real test of "without unintended timezone shifts": the column
        // itself, read under a session fourteen hours ahead of UTC and one
        // eleven behind it. A timestamp would answer two different days here;
        // a date answers the day it is.
        foreach (var zone in (string[])["Pacific/Kiritimati", "Pacific/Niue"])
        {
            await using var connection = new NpgsqlConnection(board.Instance.ConnectionString);
            await connection.OpenAsync(Token);

            await using (var setting = connection.CreateCommand())
            {
                setting.CommandText = $"set time zone '{zone}'";
                await setting.ExecuteNonQueryAsync(Token);
            }

            await using var reading = connection.CreateCommand();
            reading.CommandText = "select due_on::text from tasks limit 1";

            Assert.Equal("2026-09-14", (string?)await reading.ExecuteScalarAsync(Token));
        }
    }

    [Fact]
    public async Task A_stale_change_to_content_to_state_and_to_order_are_all_refused()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var one = await board.CaptureAsync(list.Id, "eins", Token);
        await board.CaptureAsync(list.Id, "zwei", Token);

        // Somebody else got there first.
        await board.ChangeAsync(one.Id, one.Version, Token, title: "eins, geändert");

        foreach (var stale in new[]
                 {
                     await board.ChangeAsync(one.Id, one.Version, Token, title: "aus dem Nichts"),
                     await board.ChangeAsync(one.Id, one.Version, Token, completed: true),
                     await board.ChangeAsync(one.Id, one.Version, Token, toTheTop: true),
                 })
        {
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
            Assert.Equal("stale", stale.Code);
        }

        var settled = (await board.TasksAsync(list.Id, Token)).Body!["items"]!.AsArray();

        Assert.Equal(["eins, geändert", "zwei"], settled.Select(task => task!["title"]!.GetValue<string>()));
        Assert.False(settled[0]!["completed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Moving_one_task_leaves_every_other_holder_where_they_were()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);

        var one = await board.CaptureAsync(list.Id, "eins", Token);
        var two = await board.CaptureAsync(list.Id, "zwei", Token);
        var three = await board.CaptureAsync(list.Id, "drei", Token);

        await board.ChangeAsync(three.Id, three.Version, Token, toTheTop: true);

        // The two that did not move still take the versions they were read at,
        // which is the whole reason a position is a number between its
        // neighbours rather than a place in a renumbered list.
        Assert.Equal(HttpStatusCode.OK, (await board.ChangeAsync(one.Id, one.Version, Token)).Status);
        Assert.Equal(HttpStatusCode.OK, (await board.ChangeAsync(two.Id, two.Version, Token)).Status);
    }

    [Fact]
    public async Task Every_address_refuses_a_credential_that_may_not_reach_tasks()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        using var stranger = board.Instance.CreateClient();

        foreach (var address in (string[])
                 [
                     "/api/tasks/lists",
                     $"/api/tasks/lists/{list.Id}/tasks",
                     $"/api/tasks/{task.Id}",
                 ])
        {
            using var response = await stranger.GetAsync(address, Token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var none = await board.AnAgentReachingAsync("none", Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await board.ListsAsync(Token, none)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await board.TasksAsync(list.Id, Token, none)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await board.ReadAsync(task.Id, Token, none)).Status);

        using var reader = await board.AnAgentReachingAsync("read", Token);

        Assert.Equal(HttpStatusCode.OK, (await board.ReadAsync(task.Id, Token, reader)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await board.ChangeAsync(task.Id, task.Version, Token, completed: true, asWhom: reader)).Status);
    }

    [Fact]
    public async Task A_deleted_list_is_recoverable_until_its_retention_runs_out_and_then_is_not()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        await board.DiscardListAsync(list.Id, list.Version, Token);

        // Within the period: in the Trash, with its tasks.
        Assert.Single((await board.TrashAsync(Token))["items"]!.AsArray());
        Assert.Equal("deleted", (await board.ReadAsync(task.Id, Token)).Code);
        Assert.Equal(0, (await board.PurgeAsync(Token)).Total);

        var entry = (await board.TrashAsync(Token))["items"]!.AsArray().Single()!;

        Assert.Equal(HttpStatusCode.OK, (await board.RestoreAsync(list.Id, Versions.Of(entry), Token)).Status);
        Assert.Equal(["Milch holen"], await board.OrderAsync(list.Id, Token));

        // And past it: the list and its tasks together.
        var again = (await board.ListsAsync(Token)).Body!["items"]!.AsArray().Single()!;

        await board.DiscardListAsync(list.Id, Versions.Of(again), Token);
        await board.BackdateAsync(TimeSpan.FromDays(31), Token);

        Assert.Equal(2, (await board.PurgeAsync(Token)).Total);
        Assert.Empty((await board.TrashAsync(Token))["items"]!.AsArray());
        Assert.Equal("not-found", (await board.ReadAsync(task.Id, Token)).Code);
    }

    [Fact]
    public async Task A_name_taken_while_a_list_was_in_the_trash_is_a_conflict_that_offers_another()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);

        await board.DiscardListAsync(list.Id, list.Version, Token);
        await board.MakeListAsync("Einkauf", Token);

        var entry = (await board.TrashAsync(Token))["items"]!.AsArray().Single()!;
        var version = Versions.Of(entry);

        var refused = await board.RestoreAsync(list.Id, version, Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);

        // The same call takes the name to put it back under, so nobody is ever
        // stuck with something they cannot get out of the Trash — the rule
        // `Restoration` wrote down, applied by the one module that has no tree.
        var restored = await board.RestoreAsync(list.Id, version, Token, restoreAs: "Einkauf (alt)");

        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.Equal("Einkauf (alt)", restored.Body!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task What_the_owner_switched_off_is_absent_from_every_surface_and_kept()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);
        var deleted = await board.CaptureAsync(list.Id, "weg damit", Token);

        await board.DiscardAsync(deleted.Id, deleted.Version, Token);
        await board.SwitchAsync(enabled: false, Token);

        Assert.Equal("disabled", (await board.ListsAsync(Token)).Code);
        Assert.Equal("disabled", (await board.ReadAsync(task.Id, Token)).Code);

        // An aggregate view leaves it out rather than refusing.
        Assert.Empty((await board.TrashAsync(Token))["items"]!.AsArray());

        await board.SwitchAsync(enabled: true, Token);

        Assert.Equal(["Milch holen"], await board.OrderAsync(list.Id, Token));
        Assert.Single((await board.TrashAsync(Token))["items"]!.AsArray());
    }

    [Fact]
    public async Task The_trash_now_carries_three_applications_and_never_the_scratchpad()
    {
        await using var board = await ATaskBoard.StartedAsync(postgres, Token);

        // Every application that has anything to set aside, in one list. The
        // Scratchpad is here too, and deliberately contributes nothing.
        var list = await board.MakeListAsync("Einkauf", Token);
        var task = await board.CaptureAsync(list.Id, "Milch holen", Token);

        await board.DiscardAsync(task.Id, task.Version, Token);

        using var captured = await board.Owner.PostAsJsonAsync(
            "/api/scratchpad/entries", new { text = "etwas, das verschwindet" }, Token);

        var entry = JsonNode.Parse(await captured.Content.ReadAsStringAsync(Token))!;

        using var discarded = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/scratchpad/entries/{entry["id"]!.GetValue<Guid>()}");

        discarded.Headers.TryAddWithoutValidation("If-Match", Versions.Of(entry));

        using var gone = await board.Owner.SendAsync(discarded, Token);
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);

        var items = (await board.TrashAsync(Token))["items"]!.AsArray();

        Assert.Single(items);
        Assert.Equal("tasks", items[0]!["application"]!.GetValue<string>());
    }
}
