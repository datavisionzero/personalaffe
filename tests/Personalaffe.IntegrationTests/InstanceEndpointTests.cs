using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Personalaffe.Api.Http;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The one operation outside the door, the header every answer carries, and
/// what an address under the prefix that no endpoint took answers.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InstanceEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_version_operation_answers_what_this_build_calls_itself()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var response = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        // The field is spelled the way the contract spells it, and the value is
        // the one the header carries.
        Assert.Equal("0.0.0-dev", body!["version"]!.GetValue<string>());
        Assert.Equal(body["version"]!.GetValue<string>(), response.Headers.GetValues(VersionHeader.Name).Single());
    }

    [Fact]
    public async Task Every_answer_carries_the_version_including_a_refused_one()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var refused = await client.GetAsync("/api/nothing-here", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal("0.0.0-dev", refused.Headers.GetValues(VersionHeader.Name).Single());
    }

    [Fact]
    public async Task An_unknown_address_under_the_prefix_is_an_api_error_and_not_a_page()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var response = await client.GetAsync("/api/tasks/42", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Problems.ContentType, response.Content.Headers.ContentType?.MediaType);

        var document = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        Assert.Equal("/problems/not-found", document!["type"]!.GetValue<string>());
        Assert.Equal(404, document["status"]!.GetValue<int>());
        Assert.Equal("/api/tasks/42", document["instance"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_operations_outside_the_door_carry_no_owner_data()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        foreach (var address in (string[])["/api/version", "/api/health/live", "/api/health/ready"])
        {
            var body = await client.GetStringAsync(address, TestContext.Current.CancellationToken);

            // Three operations answer before anything has authenticated, and
            // between them they say a version and two words. Anything naming the
            // database, a credential or a path on the host would be a leak from
            // the one part of the API a stranger can always reach.
            Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/Users", body, StringComparison.Ordinal);
            Assert.True(body.Length < 200, $"{address} answered {body.Length} bytes; it should answer a word.");
        }
    }
}
