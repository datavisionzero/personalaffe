using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Personalaffe.Application.Acts;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The contract an instance serves is the same contract whatever
/// <c>ASPNETCORE_ENVIRONMENT</c> says (<c>docs/operations.md</c>, "What the
/// environment does not decide").
/// </summary>
/// <remarks>
/// <para>
/// Every other suite in this project runs against Development, because that is
/// what <c>WebApplicationFactory</c> starts, and the image runs against
/// Production. That difference is not academic: it hid an empty <c>400</c> in
/// every shipped build for seven epics. <c>RouteHandlerOptions.ThrowOnBadRequest</c>
/// defaults to on in Development and off everywhere else, and off means a body
/// minimal APIs cannot bind is answered by the framework itself — so
/// <c>Problems.Handler</c> never ran, <c>Problems.Unreadable</c> was dead code
/// in production, and the five suites asserting <c>unknown-field</c> asserted a
/// contract that only held where nobody installs it.
/// </para>
/// <para>
/// <strong>These tests are worth nothing unless the instance really is
/// Production</strong>, so each of them says so out loud first. A lever that
/// silently stopped working would otherwise leave a file of tests that pass by
/// testing Development twice.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheEnvironmentDecidesNothingTests(PostgresFixture postgres)
{
    private const string Address = "owner@example.com";

    [Fact]
    public async Task A_field_the_object_does_not_define_is_said_out_loud_to_the_image_too()
    {
        await using var instance = await AnInstance.StartedAsAsync(postgres, AnInstance.Image);
        using var client = instance.CreateClient();

        AssertStartedAsTheImageIs(instance);

        using var content = new StringContent(
            $$"""{"email":"{{Address}}","passwrd":"a-long-enough-password"}""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync("/api/setup", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The document, not just the status. An empty 400 is what this was.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await ProblemOf(response);

        Assert.Equal("/problems/unknown-field", problem["type"]!.GetValue<string>());
        Assert.Equal("The request contains a field this object does not define", problem["title"]!.GetValue<string>());
        Assert.Equal(400, problem["status"]!.GetValue<int>());
        Assert.Equal("/api/setup", problem["instance"]!.GetValue<string>());

        // The extension member the code carries (docs/api.md, Errors).
        Assert.Equal("passwrd", problem["field"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_body_the_reader_cannot_make_sense_of_is_a_document_to_the_image_too()
    {
        await using var instance = await AnInstance.StartedAsAsync(postgres, AnInstance.Image);
        using var client = instance.CreateClient();

        AssertStartedAsTheImageIs(instance);

        // A number where the object takes a string: the reader gives up on a
        // field, and the refusal is named after it.
        using var content = new StringContent(
            $$"""{"email":"{{Address}}","password":7}""", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/setup", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await ProblemOf(response);

        Assert.Equal("/problems/validation", problem["type"]!.GetValue<string>());
        Assert.NotNull(problem["errors"]!["password"]);
    }

    [Fact]
    public async Task Malformed_json_is_a_document_to_the_image_too()
    {
        await using var instance = await AnInstance.StartedAsAsync(postgres, AnInstance.Image);
        using var client = instance.CreateClient();

        AssertStartedAsTheImageIs(instance);

        // Not JSON at all, so the reader gives up before any field: the refusal
        // is about the body itself.
        using var content = new StringContent("{not json", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/setup", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await ProblemOf(response);

        Assert.Equal("/problems/validation", problem["type"]!.GetValue<string>());
        Assert.NotNull(problem["errors"]!["body"]);
    }

    [Fact]
    public async Task A_bug_is_still_a_bug_and_a_refusal_is_still_a_refusal()
    {
        await using var instance = await AnInstance.StartedAsAsync(postgres, AnInstance.Image);
        using var client = instance.CreateClient();

        AssertStartedAsTheImageIs(instance);

        // The other half of what the environment could have decided: the
        // developer exception page is in the pipeline in Development and not
        // here, and what a caller is told has to be the same either way. A
        // refusal an act threw, through the same handler, on an instance
        // configured the way the image is.
        using var response = await client.PostAsJsonAsync(
            "/api/setup",
            new { email = "not an address", password = "a-long-enough-password" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await ProblemOf(response);

        Assert.Equal("/problems/validation", problem["type"]!.GetValue<string>());
        Assert.NotNull(problem["errors"]!["email"]);
    }

    [Fact]
    public async Task The_contract_the_image_serves_is_the_one_that_is_checked_in()
    {
        await using var instance = await AnInstance.StartedAsAsync(postgres, AnInstance.Image);
        using var client = instance.CreateClient();

        AssertStartedAsTheImageIs(instance);

        // ContractTests makes this comparison against a Development instance,
        // and the template this foundation came from maps the document in
        // Development only. Here it is mapped unconditionally on purpose — it is
        // what a client compiles against and what CI captures — so the image has
        // to serve the same bytes, and a build that quietly gated it would be a
        // 404 nobody found until a client was generated against nothing.
        var served = JsonNode.Parse(
            await client.GetStringAsync("/api/openapi/v1.json", TestContext.Current.CancellationToken))!;

        var checkedIn = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot.Path, "docs", "api", "openapi.json"),
            TestContext.Current.CancellationToken));

        Assert.True(
            JsonNode.DeepEquals(served, checkedIn),
            "The image serves a contract other than the one a Development instance serves.");
    }

    [Fact]
    public async Task The_service_graph_is_validated_where_the_image_runs_and_not_only_where_tests_do()
    {
        await using var instance = await AnInstance.StartedAsAsync(postgres, AnInstance.Image);

        // Building the host is the ValidateOnBuild half: a registration this
        // instance could not construct would have thrown above rather than at
        // the first request that wanted it.
        AssertStartedAsTheImageIs(instance);

        // And this is the ValidateScopes half, which is the one that can be
        // asked. Off — which is the default outside Development — a scoped act
        // resolved from the root provider is handed out and lives as long as the
        // process, holding a DbContext with it. On, it is a refusal.
        var captured = Assert.Throws<InvalidOperationException>(
            () => instance.Services.GetRequiredService<SetUpTheInstance>());

        Assert.Contains("scoped", captured.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// That the lever worked. Without it every assertion in this file would be
    /// made against Development a second time.
    /// </summary>
    private static void AssertStartedAsTheImageIs(AnInstance instance) =>
        Assert.Equal(
            AnInstance.Image,
            instance.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

    private static async Task<JsonNode> ProblemOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
        ?? throw new InvalidOperationException("The refusal carried no body at all.");
}
