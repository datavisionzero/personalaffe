using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Personalaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class KnowledgeBacklinkTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Backlinks_follow_ids_and_exclude_deleted_sources()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workspace = await AWorkspace.StartedAsync(postgres, token);
        var target = await workspace.PageAsync("Destination", "Text", token);
        var source = await workspace.PageAsync("Source", $"[Destination](page:{target})", token);
        var result = await workspace.Owner.GetFromJsonAsync<JsonObject>($"/api/knowledge/pages/{target}/backlinks", token);
        Assert.Equal(source, result!["pages"]!.AsArray().Single()!["id"]!.GetValue<Guid>());
        using var anonymous = workspace.Instance.CreateClient();
        using var denied = await anonymous.GetAsync($"/api/knowledge/pages/{target}/backlinks", token);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await workspace.DeleteAsync($"/api/knowledge/pages/{source}", token);
        result = await workspace.Owner.GetFromJsonAsync<JsonObject>($"/api/knowledge/pages/{target}/backlinks", token);
        Assert.Empty(result!["pages"]!.AsArray());
        await workspace.DeleteAsync($"/api/knowledge/pages/{target}", token);
        using var gone = await workspace.Owner.GetAsync($"/api/knowledge/pages/{target}/backlinks", token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }
}
