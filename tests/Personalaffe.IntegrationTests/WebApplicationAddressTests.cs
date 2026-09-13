using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The line <c>docs/codebase.md</c> draws: the API is everything under
/// <c>/api</c>, and every other address belongs to the web application.
/// </summary>
/// <remarks>
/// The first test is the one that keeps holding: an endpoint mapped outside the
/// group fails it whatever it is called, which is what makes <c>/files</c>,
/// <c>/tasks</c> and <c>/pages</c> safe for the application to own later.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class WebApplicationAddressTests(PostgresFixture postgres)
{
    /// <summary>What the SPA fallback matches: every path the API did not take.</summary>
    private const string Fallback = "/{*path:nonfile}";

    [Fact]
    public async Task Nothing_but_the_fallback_is_mapped_outside_the_api()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);

        // The host is built on the first client, and the routes with it.
        using var client = instance.CreateClient();
        await client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        var outside = instance.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Where(pattern => pattern != Routes.Api && !pattern.StartsWith($"{Routes.Api}/", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([Fallback], outside);
    }

    [Fact]
    public async Task An_address_of_the_application_is_never_answered_as_an_api_error()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        // The addresses the four applications will want, and one the instance
        // has no idea about. None of them is the API's, whether or not a built
        // web application is sitting in wwwroot to answer them.
        foreach (var address in (string[])["/", "/scratchpad", "/knowledge/architecture", "/tasks", "/files", "/settings"])
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.NotEqual(Problems.ContentType, response.Content.Headers.ContentType?.MediaType);
        }

        // The same word under the prefix is the API's, and answers as one.
        using var api = await client.GetAsync("/api/tasks", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Equal(Problems.ContentType, api.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_contract_is_unchanged_by_the_web_application_being_in_front_of_it()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var response = await client.GetAsync("/api/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
