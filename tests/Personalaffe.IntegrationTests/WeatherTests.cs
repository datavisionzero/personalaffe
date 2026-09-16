using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;
using Personalaffe.Application.Ports;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The one thing in this product that comes from outside it: where the owner
/// said, what it is doing there, and every way it can fail to say without
/// taking anything else down.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WeatherTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_fresh_instance_has_nowhere_and_says_so_rather_than_refusing()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        var weather = await ReadAsync(workspace);

        Assert.Null(weather["place"]);
        Assert.Null(weather["reading"]);
        Assert.True(weather["available"]!.GetValue<bool>());
        Assert.Equal("metric", weather["units"]!.GetValue<string>());
    }

    [Fact]
    public async Task Saying_where_answers_with_the_weather_there()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        var weather = await SetAsync(workspace, "Wuppertal", 51.2563, 7.1482, "metric");

        Assert.Equal("Wuppertal", weather["place"]!.GetValue<string>());
        Assert.Equal(16.1, weather["reading"]!["temperature"]!.GetValue<double>());

        // The words are the product's, in one place, so that the browser and
        // the console say the same thing about code 3.
        Assert.Equal("Overcast", weather["reading"]!["description"]!.GetValue<string>());
        Assert.Equal("°C", weather["temperature_unit"]!.GetValue<string>());
        Assert.Equal("km/h", weather["wind_unit"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_scale_reaches_the_provider_and_the_symbols_follow_it()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        var weather = await SetAsync(workspace, "Boston", 42.3601, -71.0589, "imperial");

        Assert.Equal("°F", weather["temperature_unit"]!.GetValue<string>());
        Assert.Equal("mph", weather["wind_unit"]!.GetValue<string>());
        Assert.Equal(
            Personalaffe.Domain.Weather.WeatherUnits.Imperial, sky.LastPlace!.Units);
    }

    [Fact]
    public async Task A_provider_that_does_not_answer_is_a_tile_that_does_not_know()
    {
        var silent = new AFakeSky(reading: null);
        await using var workspace = await StartedAsync(silent);

        var weather = await SetAsync(workspace, "Wuppertal", 51.2563, 7.1482, "metric");

        // Not a 503. This instance is fine; somebody else's server is not.
        Assert.Null(weather["reading"]);
        Assert.Equal("Wuppertal", weather["place"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_instance_told_not_to_ask_never_asks()
    {
        var sky = new AFakeSky(AFakeSky.Fine) { Available = false };
        await using var workspace = await StartedAsync(
            sky, new Dictionary<string, string?> { [WeatherSettings.Variable] = "off" });

        var weather = await SetAsync(workspace, "Wuppertal", 51.2563, 7.1482, "metric");

        Assert.False(weather["available"]!.GetValue<bool>());
        Assert.Null(weather["reading"]);
    }

    [Fact]
    public async Task Nothing_at_all_clears_the_place()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        await SetAsync(workspace, "Wuppertal", 51.2563, 7.1482, "metric");

        var cleared = await SetAsync(workspace, null, null, null, "metric");

        Assert.Null(cleared["place"]);
        Assert.Null(cleared["latitude"]);
        Assert.Null(cleared["reading"]);
    }

    [Fact]
    public async Task A_place_that_is_not_on_the_globe_is_refused()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        using var response = await WriteAsync(
            workspace, new { name = "Nowhere", latitude = 91.0, longitude = 0.0, units = "metric" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_write_holding_an_older_version_is_refused()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        var stale = await workspace.VersionOfAsync(
            "/api/weather", TestContext.Current.CancellationToken);

        await SetAsync(workspace, "Wuppertal", 51.2563, 7.1482, "metric");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/weather/place")
        {
            Content = JsonContent.Create(
                new { name = "Somewhere else", latitude = 0.0, longitude = 0.0, units = "metric" }),
        };
        request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, stale);

        using var response = await workspace.Owner.SendAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task A_place_is_looked_up_by_name_so_nobody_has_to_find_two_numbers()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        var places = JsonNode.Parse(await workspace.Owner.GetStringAsync(
            "/api/weather/places?q=Wuppertal", TestContext.Current.CancellationToken))!;

        Assert.Equal(2, places["items"]!.AsArray().Count);
        Assert.Equal("Germany", places["items"]![0]!["country"]!.GetValue<string>());
        Assert.Equal(1, sky.LookedUp);
    }

    [Fact]
    public async Task An_agent_may_read_the_weather_and_may_not_decide_where_it_is_for()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        using var agent = await AnAgentAsync(workspace);

        using var read = await agent.GetAsync("/api/weather", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // Both of these decide what this instance tells an outside service
        // about the person who owns it. The write carries a version it really
        // holds, so what it is refused for is who is asking and nothing else.
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/weather/place")
        {
            Content = JsonContent.Create(
                new { name = "Somewhere", latitude = 0.0, longitude = 0.0, units = "metric" }),
        };
        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch,
            await workspace.VersionOfAsync("/api/weather", TestContext.Current.CancellationToken));

        using var written = await agent.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, written.StatusCode);

        using var looked = await agent.GetAsync(
            "/api/weather/places?q=Wuppertal", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, looked.StatusCode);
        Assert.Equal(0, sky.LookedUp);
    }

    [Fact]
    public async Task What_a_free_provider_is_paid_in_travels_with_the_answer()
    {
        var sky = new AFakeSky(AFakeSky.Fine);
        await using var workspace = await StartedAsync(sky);

        Assert.Equal(
            "Weather data by a test",
            (await ReadAsync(workspace))["attribution"]!.GetValue<string>());
    }

    private Task<AWorkspace> StartedAsync(
        AFakeSky sky, IReadOnlyDictionary<string, string?>? settings = null) =>
        AWorkspace.StartedAsync(
            postgres,
            // Appended, so the container's last registration wins and nothing
            // here has to unpick the composition root.
            services => services.AddSingleton<IWeather>(sky),
            TestContext.Current.CancellationToken,
            settings);

    private static async Task<JsonNode> ReadAsync(AWorkspace workspace) =>
        JsonNode.Parse(await workspace.Owner.GetStringAsync(
            "/api/weather", TestContext.Current.CancellationToken))!;

    private static async Task<JsonNode> SetAsync(
        AWorkspace workspace, string? name, double? latitude, double? longitude, string units)
    {
        using var response = await WriteAsync(
            workspace, new { name, latitude, longitude, units });

        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<HttpResponseMessage> WriteAsync(AWorkspace workspace, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/weather/place")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.TryAddWithoutValidation(
            EntityTags.IfMatch,
            await workspace.VersionOfAsync("/api/weather", TestContext.Current.CancellationToken));

        return await workspace.Owner.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpClient> AnAgentAsync(AWorkspace workspace)
    {
        using var granted = await workspace.Owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent",
                permissions = new
                {
                    scratchpad = "read_write",
                    knowledge = "read_write",
                    tasks = "read_write",
                    files = "read_write",
                },
            },
            TestContext.Current.CancellationToken);

        granted.EnsureSuccessStatusCode();

        var token = JsonNode.Parse(await granted.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!["token"]!.GetValue<string>();

        var client = workspace.Instance.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
