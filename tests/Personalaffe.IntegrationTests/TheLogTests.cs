using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// What the operator's log says, and what it must never say.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/operations.md</c> makes two promises about it: that the request log
/// carries method, path, status, duration and the caller's address and
/// <strong>nothing the owner or an agent wrote</strong>, and that a status in it
/// is the status the caller was given. Both were false until PERSONAL-69, for
/// one reason: <c>UseSerilogRequestLogging</c> was registered inside
/// <c>UseExceptionHandler</c>, so every <see cref="Refusal"/> an act throws
/// passed through the request logging as an unhandled exception against a
/// response that was still a 500.
/// </para>
/// <para>
/// A promise a document makes and no test holds is a promise that lasts until
/// somebody reorders two lines.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TheLogTests(PostgresFixture postgres)
{
    /// <summary>
    /// A title nobody would write by accident, so that finding it in the log is
    /// evidence and not a coincidence.
    /// </summary>
    private const string Title = "Steuererklärung 2026";

    [Fact]
    public async Task A_refusal_is_logged_as_what_the_caller_was_told_and_without_what_they_wrote()
    {
        var token = TestContext.Current.CancellationToken;
        await using var knowledge = await AKnowledgeBase.StartedAsync(postgres, token);
        var mark = knowledge.Instance.Mark;

        var first = await knowledge.WriteAsync(Title, parent: null, "nothing in particular", token);
        Assert.Equal(HttpStatusCode.Created, first.Status);

        // The same title in the same place: a conflict, which is the product
        // deciding something rather than the instance failing at something.
        var second = await knowledge.WriteAsync(Title, parent: null, "nothing in particular", token);
        Assert.Equal(HttpStatusCode.Conflict, second.Status);

        var log = string.Join("\n", knowledge.Instance.Logged);

        Assert.Contains("responded 409", log, StringComparison.Ordinal);
        Assert.DoesNotContain("responded 500", log, StringComparison.Ordinal);

        // The heart of it. The title is in the refusal's own message — where it
        // belongs, because the owner is the one who has to act on it — and the
        // refusal reaching the log is what put it in a second place.
        Assert.DoesNotContain(Title, log, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(Refusal), log, StringComparison.Ordinal);

        // And nothing about it was an incident: a conflict is an ordinary
        // answer, and an operator watching for warnings is watching for
        // something else. Since the mark, because what the host said while it
        // was starting is not what this test is about (AnInstance.Mark).
        Assert.Empty(knowledge.Instance.WarningsSince(mark));
    }

    [Theory]
    [InlineData("/api/knowledge/pages/0195c0de-0000-7000-8000-000000000000", 404)]
    [InlineData("/api/nothing-here", 404)]
    public async Task Every_refusal_is_logged_at_the_status_its_caller_saw(string address, int status)
    {
        var token = TestContext.Current.CancellationToken;
        await using var knowledge = await AKnowledgeBase.StartedAsync(postgres, token);
        var mark = knowledge.Instance.Mark;

        using var response = await knowledge.Owner.GetAsync(address, token);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Contains(
            $"responded {status}",
            string.Join("\n", knowledge.Instance.Logged),
            StringComparison.Ordinal);
        Assert.Empty(knowledge.Instance.WarningsSince(mark));
    }

    [Fact]
    public async Task A_stale_write_is_an_ordinary_line_too()
    {
        var token = TestContext.Current.CancellationToken;
        await using var knowledge = await AKnowledgeBase.StartedAsync(postgres, token);
        var mark = knowledge.Instance.Mark;

        // No If-Match at all, which is the guard refusing rather than a version
        // that has moved — the same refusal by the shortest route to it.
        using var request = new HttpRequestMessage(
            HttpMethod.Put, "/api/applications/knowledge")
        {
            Content = JsonContent.Create(new { enabled = false }),
        };

        using var response = await knowledge.Owner.SendAsync(request, token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Contains(
            "responded 412",
            string.Join("\n", knowledge.Instance.Logged),
            StringComparison.Ordinal);
        Assert.Empty(knowledge.Instance.WarningsSince(mark));
    }

    [Fact]
    public async Task The_line_says_who_asked()
    {
        var token = TestContext.Current.CancellationToken;
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        using var response = await client.GetAsync("/api/version", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The test host's caller has no address at all, and "unknown" is what
        // this instance says for one — the same word the throttle on failed
        // sign-ins keys on. What matters here is that the line has the field:
        // that it carries the forwarded address behind a named proxy is
        // TrustedProxiesTests', and it is the same value.
        Assert.Contains(
            instance.Logged,
            line => line.Contains("responded 200", StringComparison.Ordinal)
                    && line.Contains(" to ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_real_fault_is_still_one_error_with_its_exception_behind_it()
    {
        var token = TestContext.Current.CancellationToken;

        await using var instance = await AnInstance.StartedWithTrashAsync(
            postgres, new AFaultyTrash());

        var owner = await AnOwner.SignedInAsync(instance, token);

        using var response = await owner.GetAsync("/api/trash", token);

        // What the caller gets for a bug is a title and a status, and nothing
        // out of the exception — that part was never broken and this is what
        // keeps it that way.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!;

        Assert.Equal("/problems/internal", document["type"]!.GetValue<string>());
        Assert.Null(document["detail"]);
        Assert.DoesNotContain(
            AFaultyTrash.Message,
            document.ToJsonString(),
            StringComparison.Ordinal);

        // And what the operator gets is the exception, once, above information.
        Assert.Contains(
            instance.Warnings,
            line => line.Contains(AFaultyTrash.Message, StringComparison.Ordinal));
    }

    /// <summary>
    /// A contributor that is simply broken — not a refusal, which is the product
    /// deciding something, but the kind of fault that is nobody's decision.
    /// </summary>
    private sealed class AFaultyTrash : ITrash
    {
        public const string Message = "the sort of thing that is a bug";

        public WorkspaceApplication Application => WorkspaceApplication.Knowledge;

        public Task<IReadOnlyList<TrashEntry>> ListAsync(int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task<RestoredTo?> RestoreAsync(
            Guid id, ContentVersion held, string? restoreAs, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task<bool> RemoveAsync(Guid id, ContentVersion held, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task<int> EmptyAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task<int> PurgeAsync(DateTimeOffset expiredBefore, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
