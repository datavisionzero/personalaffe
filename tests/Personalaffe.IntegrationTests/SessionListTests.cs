using System.Net;
using System.Text.Json.Nodes;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Where this instance is signed in, and the two ways of ending one of those.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SessionListTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_list_says_which_one_is_asking()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        using var elsewhere = await AnOwner.SignInAgainAsync(instance, TestContext.Current.CancellationToken);

        var sessions = await Listed(client);

        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, session => session!["current"]!.GetValue<bool>());
        Assert.All(sessions, session =>
            Assert.Equal(AnOwner.Browser, session!["description"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Ending_the_others_leaves_the_one_that_asked()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        using var elsewhere = await AnOwner.SignInAgainAsync(instance, TestContext.Current.CancellationToken);

        using var ended = await client.DeleteAsync("/api/sessions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        using var theirs = await elsewhere.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, theirs.StatusCode);

        using var mine = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        Assert.Single(await Listed(client));
    }

    [Fact]
    public async Task Ending_one_by_name_ends_that_one()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        using var elsewhere = await AnOwner.SignInAgainAsync(instance, TestContext.Current.CancellationToken);

        var theirs = (await Listed(client))
            .First(session => !session!["current"]!.GetValue<bool>())!["id"]!.GetValue<string>();

        using var ended = await client.DeleteAsync(
            $"/api/sessions/{theirs}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        using var after = await elsewhere.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A_session_that_is_not_there_is_not_there()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var response = await client.DeleteAsync(
            $"/api/sessions/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_revoked_session_is_off_the_list()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        using var elsewhere = await AnOwner.SignInAgainAsync(instance, TestContext.Current.CancellationToken);

        using var out_ = await elsewhere.DeleteAsync("/api/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, out_.StatusCode);

        Assert.Single(await Listed(client));
    }

    private static async Task<IReadOnlyList<JsonNode?>> Listed(HttpClient client)
    {
        var body = await client.GetStringAsync("/api/sessions", TestContext.Current.CancellationToken);

        return [.. JsonNode.Parse(body)!.AsArray()];
    }
}
