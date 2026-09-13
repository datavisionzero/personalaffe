using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Personalaffe.Api.Http;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// What a refusal looks like on the wire, and what a bug does not look like.
/// </summary>
/// <remarks>
/// The handler is exercised directly rather than through an endpoint that
/// throws on purpose: the instance has no such endpoint, and adding one to make
/// a test possible would put it in the contract and in the image.
/// </remarks>
public sealed class ProblemTests
{
    [Fact]
    public void Every_code_has_a_status_and_a_title()
    {
        foreach (var code in Enum.GetValues<RefusalCode>())
        {
            Assert.InRange(Problems.StatusOf(code), 400, 599);
            Assert.NotEmpty(Problems.TitleOf(code));
            Assert.StartsWith("/problems/", Problems.TypeOf(code), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(RefusalCode.Validation, "validation")]
    [InlineData(RefusalCode.UnknownField, "unknown-field")]
    [InlineData(RefusalCode.NotFound, "not-found")]
    [InlineData(RefusalCode.Stale, "stale")]
    public void A_code_is_spelled_on_the_wire_the_way_a_client_switches_on_it(RefusalCode code, string expected)
    {
        Assert.Equal(expected, Problems.CodeOf(code));
        Assert.Equal($"/problems/{expected}", Problems.TypeOf(code));
    }

    [Fact]
    public async Task A_refusal_reaches_the_caller_as_what_it_said()
    {
        var document = await HandledAsync(
            Refusal.Validation("title", "A title is at most 200 characters."));

        Assert.Equal("/problems/validation", document["type"]!.GetValue<string>());
        Assert.Equal(400, document["status"]!.GetValue<int>());
        Assert.Contains("200 characters", document["detail"]!.GetValue<string>(), StringComparison.Ordinal);

        // The extension member the code carries, keyed the way the wire spells
        // it, so that nothing between Domain and the document renames it.
        Assert.Equal(
            ["A title is at most 200 characters."],
            document["errors"]!["title"]!.AsArray().Select(message => message!.GetValue<string>()));
    }

    [Fact]
    public async Task A_bug_reaches_the_caller_as_a_status_and_nothing_else()
    {
        const string Secret = "Host=db;Password=the-one-thing-that-must-not-travel";

        var document = await HandledAsync(new InvalidOperationException(Secret));
        var written = document.ToJsonString();

        Assert.Equal("/problems/internal", document["type"]!.GetValue<string>());
        Assert.Equal(500, document["status"]!.GetValue<int>());

        // Neither the message, nor the type of the exception, nor a frame of its
        // stack. What the operator needs is in the log; what the caller gets is
        // that something went wrong.
        Assert.Null(document["detail"]);
        Assert.DoesNotContain("Password", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", written, StringComparison.Ordinal);
        Assert.DoesNotContain("at Personalaffe", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_the_framework_could_not_read_is_the_caller_s_mistake()
    {
        var document = await HandledAsync(new BadHttpRequestException("Unreadable."));

        Assert.Equal("/problems/validation", document["type"]!.GetValue<string>());
        Assert.Equal(400, document["status"]!.GetValue<int>());
        Assert.NotNull(document["errors"]!["body"]);
    }

    /// <summary>Runs one exception through the handler and reads what it wrote.</summary>
    private static async Task<JsonObject> HandledAsync(Exception exception)
    {
        var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Request.Path = "/api/somewhere";
        context.Response.Body = body;

        var handled = await new Problems.Handler(NullLogger<Problems.Handler>.Instance)
            .TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

        Assert.True(handled);

        body.Position = 0;
        return (JsonObject)JsonNode.Parse(body, documentOptions: new JsonDocumentOptions())!;
    }
}
