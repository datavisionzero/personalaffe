using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Http;
using Personalaffe.Application.Acts.Scratchpad;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Infrastructure.Persistence;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// PERSONAL-E3's claims, checked against the contract itself rather than a list
/// somebody maintains here — the shape <see cref="TheDoorHoldsTests"/> gave the
/// epic before it.
/// </summary>
/// <remarks>
/// A content epic that adds a guarded write or a destructive operation is
/// covered by this the day it adds it, and one that quietly loses its guard
/// turns this red rather than being noticed by somebody reading a diff.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheSafeguardsHoldTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Actor TheOwner = new() { Kind = CallerKind.Owner, Id = Guid.CreateVersion7() };

    /// <summary>Every operation the contract says takes a version.</summary>
    public static TheoryData<string, string> EveryGuardedWrite
    {
        get
        {
            var guarded = new TheoryData<string, string>();

            foreach (var (method, path, operation) in Contract())
            {
                if (Parameters(operation).Any(parameter =>
                        parameter?["in"]?.GetValue<string>() == "header"
                        && parameter["name"]?.GetValue<string>() == EntityTags.IfMatch))
                {
                    guarded.Add(method, path);
                }
            }

            return guarded;
        }
    }

    /// <summary>Every operation that destroys something the owner could have had back.</summary>
    public static TheoryData<string, string> EveryDestruction
    {
        get
        {
            var destructive = new TheoryData<string, string>();

            foreach (var (method, path, _) in Contract())
            {
                if (method == "DELETE" && path.StartsWith("/api/trash", StringComparison.Ordinal))
                {
                    destructive.Add(method, path);
                }
            }

            return destructive;
        }
    }

    [Fact]
    public void The_contract_has_guarded_writes_and_destructions_to_check()
    {
        // Without this, an epic that removed the Trash would leave two theories
        // with nothing to drive and a suite that passes by having no subject.
        Assert.NotEmpty(EveryGuardedWrite);
        Assert.NotEmpty(EveryDestruction);
    }

    [Theory]
    [MemberData(nameof(EveryGuardedWrite))]
    public async Task A_write_that_says_nothing_about_what_it_replaces_is_refused(string method, string path)
    {
        await using var instance = await Holding();
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        using var response = await Sent(owner, method, path, ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("/problems/stale", await TypeOf(response));
    }

    [Theory]
    [MemberData(nameof(EveryGuardedWrite))]
    public async Task A_write_holding_something_that_is_not_a_version_is_refused_the_same_way(
        string method, string path)
    {
        await using var instance = await Holding();
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        foreach (var forged in new[] { "*", "\"yesterday\"", "W/\"2026-09-14T08:30:00.123456Z\"" })
        {
            using var response = await Sent(owner, method, path, forged);

            Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(EveryDestruction))]
    public async Task Nothing_an_agent_can_be_given_destroys_anything(string method, string path)
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);

        await using var instance = await Holding(knowledge);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var entry = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);

        // Read/write everywhere the owner can grant, which is everything there
        // is to grant.
        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new
            {
                name = "an agent with everything",
                permissions = new
                {
                    scratchpad = "read_write",
                    knowledge = "read_write",
                    tasks = "read_write",
                    files = "read_write",
                },
            },
            Token);

        var token = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!["token"]!.GetValue<string>();

        using var agent = instance.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", token);

        using var response = await Sent(
            agent, method, path, EntityTags.For(ContentVersion.Of(entry.UpdatedAt)), entry.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(knowledge.Removed);
        Assert.Equal(0, knowledge.Emptied);
    }

    [Fact]
    public async Task An_agent_that_may_only_read_changes_nothing_and_sees_only_its_own_half()
    {
        var knowledge = new AFakeTrash(WorkspaceApplication.Knowledge);
        var files = new AFakeTrash(WorkspaceApplication.Files);

        await using var instance = await Holding(knowledge, files);
        using var owner = await AnOwner.SignedInAsync(instance, Token);

        var page = knowledge.Holding("architecture", DateTimeOffset.UtcNow, TheOwner);
        files.Holding("invoice.pdf", DateTimeOffset.UtcNow, TheOwner);

        using var granted = await owner.PostAsJsonAsync(
            "/api/agents",
            new { name = "a reader", permissions = new { knowledge = "read" } },
            Token);

        var token = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!["token"]!.GetValue<string>();

        using var agent = instance.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var seen = (await agent.GetFromJsonAsync<JsonNode>("/api/trash", Token))!["items"]!.AsArray();

        Assert.Equal(["architecture"], seen.Select(entry => entry!["name"]!.GetValue<string>()));

        using var refused = await Sent(
            agent,
            "POST",
            "/api/trash/knowledge/{id}/restore",
            EntityTags.For(ContentVersion.Of(page.UpdatedAt)),
            page.Id);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Empty(knowledge.Restored);
    }

    [Fact]
    public void The_two_404s_are_one_status_and_two_answers()
    {
        // Deleted content and nothing at all are the same status and different
        // types, which is the distinction `deleted` exists to make: one of them
        // means the owner can have the thing back.
        Assert.Equal(Problems.StatusOf(RefusalCode.NotFound), Problems.StatusOf(RefusalCode.Deleted));
        Assert.NotEqual(Problems.TypeOf(RefusalCode.NotFound), Problems.TypeOf(RefusalCode.Deleted));
        Assert.Equal("/problems/deleted", Problems.TypeOf(RefusalCode.Deleted));
    }

    [Fact]
    public void Nothing_in_the_purge_can_be_told_which_applications_to_skip()
    {
        // PERSONAL-E4 adds the switch that hides an application. Disabling one
        // must not suspend a retention deadline, and re-enabling it must not
        // resurrect what expired while it was off — so the port it would have
        // to be passed through has no room for it. This is the test that says
        // so out loud.
        var purge = typeof(ITrash).GetMethod(nameof(ITrash.PurgeAsync))!;

        Assert.Equal(
            [typeof(DateTimeOffset), typeof(CancellationToken)],
            purge.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void Nothing_in_the_scratchpad_sweep_can_be_told_which_applications_to_skip()
    {
        // The same claim as the Trash's purge above, asserted the same way. The
        // Scratchpad's sweep runs whether the owner has the application
        // switched on or off, and works from a deadline somebody else worked
        // out — so neither the switch nor a retention has a parameter here to
        // arrive through.
        foreach (var sweep in new[]
        {
            typeof(IScratchpadEntries).GetMethod(nameof(IScratchpadEntries.ExpireAsync))!,
            typeof(ExpireTheEntries).GetMethod(nameof(ExpireTheEntries.ExecuteAsync))!,
        })
        {
            Assert.Equal(
                [typeof(DateTimeOffset), typeof(CancellationToken)],
                sweep.GetParameters().Select(parameter => parameter.ParameterType));
        }
    }

    [Fact]
    public void The_scratchpad_contributes_nothing_to_the_trash()
    {
        // The one application that deliberately does not inherit the Trash
        // (`docs/api.md`, Deleting sets content aside). The day somebody wires
        // it in by habit, a deletion the owner was told is final stops being
        // final — so the absence is asserted rather than remembered.
        Assert.False(typeof(ITrash).IsAssignableFrom(typeof(ScratchpadEntries)));

        Assert.DoesNotContain(
            typeof(IScratchpadEntries).GetMethods().Select(method => method.Name),
            name => name is "RestoreAsync" or "PurgeAsync" or "EmptyAsync");
    }

    private Task<AnInstance> Holding(params AFakeTrash[] contributors) =>
        AnInstance.StartedWithAsync(postgres, services =>
        {
            foreach (var contributor in contributors)
            {
                services.AddSingleton<ITrash>(contributor);
            }
        });

    private static IEnumerable<(string Method, string Path, JsonNode Operation)> Contract()
    {
        var document = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot.Path, "docs", "api", "openapi.json")))!;

        foreach (var (path, methods) in document["paths"]!.AsObject())
        {
            foreach (var (method, operation) in methods!.AsObject())
            {
                yield return (method.ToUpperInvariant(), path, operation!);
            }
        }
    }

    private static IEnumerable<JsonNode?> Parameters(JsonNode operation) =>
        operation["parameters"]?.AsArray() ?? [];

    private static async Task<HttpResponseMessage> Sent(
        HttpClient client, string method, string path, string? ifMatch, Guid? id = null)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            path
                .Replace("{application}", "knowledge", StringComparison.Ordinal)
                .Replace("{id}", (id ?? Guid.CreateVersion7()).ToString(), StringComparison.Ordinal))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(EntityTags.IfMatch, ifMatch);
        }

        request.Headers.Add("X-Personalaffe-CSRF", "1");
        request.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        return await client.SendAsync(request, Token);
    }

    private static async Task<string> TypeOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["type"]!.GetValue<string>();
}
