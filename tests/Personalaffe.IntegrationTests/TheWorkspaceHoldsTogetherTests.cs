using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Application.Ports;
using Personalaffe.Domain.Scratchpad;
using Personalaffe.Domain.Weather;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// PERSONAL-E9's own suite: the two things that are about the workspace rather
/// than about one application.
/// </summary>
/// <remarks>
/// <para>
/// The search's and the dashboard's own behaviour is proved by
/// <see cref="SearchTests"/> and <see cref="DashboardTests"/>. What is here is
/// what neither of them can say on its own: that the two agree with each other
/// about what a caller may see, that a finding is a thing that is really there,
/// that what the instance destroyed or set aside leaves both of them, and that
/// a provider on the other side of the internet cannot hold up the owner's home
/// page.
/// </para>
/// <para>
/// It is the fourth suite of this shape, after the door's, the safeguards' and
/// the three applications' — a suite per epic, closing it
/// (<c>docs/codebase.md</c>).
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheWorkspaceHoldsTogetherTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// A word that nothing else in these suites uses, so that what one search
    /// finds is what this test wrote and nothing that happened to be nearby.
    /// </summary>
    private const string Word = "pomegranate";

    [Fact]
    public async Task Every_application_is_findable_and_every_finding_is_a_thing_that_is_there()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var page = await workspace.PageAsync($"{Word} decisions", "…", Token);
        var entry = await workspace.EntryAsync($"a {Word} from the bank", Token);
        var list = await workspace.ListAsync("Work", Token);
        var task = await workspace.TaskAsync(list, $"Buy a {Word}", string.Empty, null, Token);
        var file = await workspace.FileAsync($"{Word}.pdf", Token);

        var found = (await workspace.SearchAsync(Word, Token))["items"]!.AsArray();

        Assert.Equal(
            ["files", "knowledge", "scratchpad", "tasks"],
            found.Select(one => one!["application"]!.GetValue<string>()).Order());

        // Every id is the id of something that answers at its own address. A
        // search that found a row nothing can be opened from would be a search
        // whose results are a dead end (`docs/mvp-plan.md`, PERSONAL-E9: the
        // correct stable target).
        var addresses = new Dictionary<Guid, string>
        {
            [page] = $"/api/knowledge/pages/{page}",
            [entry] = $"/api/scratchpad/entries/{entry}",
            [task] = $"/api/tasks/{task}",
            [file] = $"/api/files/{file}",
        };

        foreach (var one in found)
        {
            var id = one!["id"]!.GetValue<Guid>();

            using var opened = await workspace.Owner.GetAsync(addresses[id], Token);

            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        }

        // And what a task and a file sit in is the screen a client opens for
        // them.
        Assert.Equal(
            list,
            found.First(one => one!["application"]!.GetValue<string>() == "tasks")!
                ["within"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task What_the_sweep_destroyed_is_not_found_and_is_not_on_the_home_page()
    {
        var now = DateTimeOffset.UtcNow;
        var period = RetentionSettings.Default.Scratchpad;

        // Seeded before the instance starts, because it sweeps as soon as it is
        // up: an entry written afterwards would be racing the thing under test.
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var context = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(context).ApplyAsync(Token);

            context.ScratchpadEntries.AddRange(
                ScratchpadEntry.Capture($"an expired {Word}", false, now - period - TimeSpan.FromMinutes(1)),
                ScratchpadEntry.Capture($"a {Word} that is still here", false, now));

            await context.SaveChangesAsync(Token);
        }

        await using var instance = AnInstance.AgainstWith(
            connectionString,
            services => services.AddSingleton<TimeProvider>(new Fixed(now)));

        using var owner = await AnOwner.SignedInAsync(instance, Token);

        // Expiry destroys the row, so there is nothing left for either of them
        // to filter: the search reads the same table the sweep emptied, and so
        // does the tile.
        await Eventually(async () =>
        {
            var found = JsonNode.Parse(await owner.GetStringAsync($"/api/search?q={Word}", Token))!
                ["items"]!.AsArray();

            return found.Count == 1
                && found[0]!["title"]!.GetValue<string>().Contains("still here", StringComparison.Ordinal);
        });

        var home = JsonNode.Parse(await owner.GetStringAsync("/api/dashboard", Token))!;

        Assert.Single(home["scratchpad"]!.AsArray());
    }

    [Fact]
    public async Task Something_put_in_the_Trash_leaves_both_of_them_and_comes_back_with_the_restore()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        var page = await workspace.PageAsync($"{Word} decisions", "…", Token);

        Assert.Single((await workspace.SearchAsync(Word, Token))["items"]!.AsArray());

        await workspace.DeleteAsync($"/api/knowledge/pages/{page}", Token);

        Assert.Empty((await workspace.SearchAsync(Word, Token))["items"]!.AsArray());
        Assert.Empty(
            JsonNode.Parse(await workspace.Owner.GetStringAsync("/api/dashboard", Token))!
                ["knowledge"]!.AsArray());

        await RestoredAsync(workspace, page);

        // Nothing re-indexes it: the column is computed from the row, so the
        // row coming back out of the Trash is the whole of it coming back into
        // the search (`Configurations/SearchIndex.cs`).
        Assert.Single((await workspace.SearchAsync(Word, Token))["items"]!.AsArray());
        Assert.Single(
            JsonNode.Parse(await workspace.Owner.GetStringAsync("/api/dashboard", Token))!
                ["knowledge"]!.AsArray());
    }

    [Fact]
    public async Task The_dashboard_and_the_search_agree_about_what_an_agent_may_see()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        await workspace.PageAsync($"{Word} decisions", "…", Token);
        await workspace.EntryAsync($"a {Word} from the bank", Token);
        await workspace.FileAsync($"{Word}.pdf", Token);

        using var agent = await AnAgentAsync(workspace, knowledge: "read", files: "read");

        var found = JsonNode.Parse(await agent.GetStringAsync($"/api/search?q={Word}", Token))!
            ["items"]!.AsArray()
            .Select(one => one!["application"]!.GetValue<string>())
            .Order()
            .ToArray();

        var home = JsonNode.Parse(await agent.GetStringAsync("/api/dashboard", Token))!;

        var drawn = home["tiles"]!.AsArray()
            .Where(tile => tile!["offered"]!.GetValue<bool>())
            .Select(tile => tile!["tile"]!.GetValue<string>())
            .Order()
            .ToArray();

        // The same two applications, out of two different endpoints, decided by
        // the same permission — and the weather, which belongs to no
        // application and is therefore offered to anybody the door let in.
        Assert.Equal(["files", "knowledge"], found);
        Assert.Equal(["files", "knowledge", "weather"], drawn);

        Assert.Null(home["scratchpad"]);
        Assert.Null(home["tasks"]);
        Assert.NotNull(home["knowledge"]);
        Assert.NotNull(home["files"]);
    }

    [Fact]
    public async Task A_provider_that_never_answers_does_not_hold_up_the_home_page()
    {
        var hanging = new ASkyThatNeverAnswers();

        await using var workspace = await AWorkspace.StartedAsync(
            postgres, services => services.AddSingleton<IWeather>(hanging), Token);

        var clock = Stopwatch.StartNew();

        using var home = await workspace.Owner.GetAsync("/api/dashboard", Token);

        clock.Stop();

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        // The point of two addresses, as a number. Nothing in this request ever
        // reaches the provider, so a provider that never answers costs the home
        // page nothing at all (`docs/api.md`, The weather).
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(5),
            $"the home page took {clock.Elapsed} while the weather was hanging");

        Assert.Equal(0, hanging.Asked);
    }

    [Fact]
    public async Task A_write_an_agent_made_is_in_both_of_them_on_the_next_read()
    {
        await using var workspace = await AWorkspace.StartedAsync(postgres, Token);

        using var agent = await AnAgentAsync(workspace, knowledge: "read_write");

        using var written = await agent.PostAsJsonAsync(
            "/api/knowledge/pages",
            new { title = $"{Word} from an agent", parent = (Guid?)null, markdown = "…" },
            Token);

        Assert.Equal(HttpStatusCode.Created, written.StatusCode);

        // Nothing is pushed and nothing is invalidated: the next read is the
        // whole mechanism, and this is the server half of "changes made through
        // the CLI or another device appear without a manual reload" — the
        // browser half is `src/web/browser`.
        Assert.Single((await workspace.SearchAsync(Word, Token))["items"]!.AsArray());
        Assert.Single(
            JsonNode.Parse(await workspace.Owner.GetStringAsync("/api/dashboard", Token))!
                ["knowledge"]!.AsArray());
    }

    private static async Task RestoredAsync(AWorkspace workspace, Guid id)
    {
        var entry = JsonNode.Parse(await workspace.Owner.GetStringAsync("/api/trash", Token))!
            ["items"]!.AsArray()
            .First(one => one!["id"]!.GetValue<Guid>() == id)!;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/trash/knowledge/{id}/restore");

        request.Headers.TryAddWithoutValidation(
            Personalaffe.Api.Http.EntityTags.IfMatch,
            $"\"{Personalaffe.Api.Http.Rfc3339.Spell(entry["updated_at"]!.GetValue<DateTimeOffset>())}\"");

        using var response = await workspace.Owner.SendAsync(request, Token);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpClient> AnAgentAsync(
        AWorkspace workspace, string knowledge = "none", string files = "none")
    {
        using var granted = await workspace.Owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new { scratchpad = "none", knowledge, tasks = "none", files },
            },
            Token);

        granted.EnsureSuccessStatusCode();

        var token = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!
            ["token"]!.GetValue<string>();

        var client = workspace.Instance.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task Eventually(Func<Task<bool>> until)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await until())
            {
                return;
            }

            await Task.Delay(100, Token);
        }

        Assert.Fail("it never happened");
    }

    /// <summary>
    /// The instance's clock, set to the moment the entries were placed against,
    /// so that a boundary is a boundary rather than a margin somebody guessed.
    /// </summary>
    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// A provider that would hang for ever if anything asked it. Nothing does,
    /// and that is the assertion.
    /// </summary>
    private sealed class ASkyThatNeverAnswers : IWeather
    {
        public int Asked { get; private set; }

        public bool Available => true;

        public string Attribution => "Weather data by nobody";

        public async Task<Reading?> ReadAsync(WeatherPlace place, CancellationToken cancellationToken)
        {
            Asked++;

            await Task.Delay(Timeout.Infinite, cancellationToken);

            return null;
        }

        public Task<IReadOnlyList<SomewhereCalled>> LookUpAsync(
            string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SomewhereCalled>>([]);
    }
}
