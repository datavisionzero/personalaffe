using Microsoft.AspNetCore.Http;
using Personalaffe.Api.Http;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The guard in HTTP's words: what a read answers, what a write has to send
/// back, and every way of sending something else.
/// </summary>
/// <remarks>
/// The helper is exercised against a request and a response directly, the way
/// <see cref="ProblemTests"/> exercises the handler: this is the layer that
/// turns a header into a version, and no content endpoint exists yet to reach
/// it through. The endpoint that does carry the guard — the Trash's restore —
/// exercises it end to end, against a real instance, in TrashTests.
/// </remarks>
public sealed class EntityTagTests
{
    private static readonly ContentVersion AtHalfPast =
        ContentVersion.Of(new DateTimeOffset(2026, 9, 14, 8, 30, 0, TimeSpan.Zero).AddMicroseconds(123_456));

    [Fact]
    public void A_read_answers_the_version_as_a_strong_tag_in_the_one_timestamp_spelling()
    {
        var response = new DefaultHttpContext().Response;

        EntityTags.Write(response, AtHalfPast);

        Assert.Equal("\"2026-09-14T08:30:00.123456Z\"", response.Headers.ETag);
    }

    [Fact]
    public void The_tag_a_read_answered_is_the_version_a_write_sends_back()
    {
        var request = RequestSending(EntityTags.For(AtHalfPast));

        Assert.True(AtHalfPast.Matches(EntityTags.Required(request)));
    }

    [Fact]
    public void A_write_that_says_nothing_about_what_it_replaces_is_refused()
    {
        var refusal = Assert.Throws<Refusal>(() => EntityTags.Required(new DefaultHttpContext().Request));

        Assert.Equal(RefusalCode.Stale, refusal.Code);
        Assert.Contains("If-Match", refusal.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"2026-09-14T08:30:00.123456Z\"")]
    [InlineData("2026-09-14T08:30:00.123456Z")]
    [InlineData("\"yesterday\"")]
    [InlineData("\"\"")]
    [InlineData("")]
    public void Anything_that_is_not_a_version_this_instance_wrote_is_refused_as_stale(string sent)
    {
        var refusal = Assert.Throws<Refusal>(() => EntityTags.Required(RequestSending(sent)));

        Assert.Equal(RefusalCode.Stale, refusal.Code);
    }

    [Fact]
    public void A_write_naming_more_than_one_version_is_refused()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[EntityTags.IfMatch] = new[] { EntityTags.For(AtHalfPast), "\"2026-09-14T09:00:00.000000Z\"" };

        var refusal = Assert.Throws<Refusal>(() => EntityTags.Required(request));

        Assert.Equal(RefusalCode.Stale, refusal.Code);
        Assert.Contains("more than one", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_holding_an_older_version_is_refused_and_told_the_current_one()
    {
        var current = ContentVersion.Of(AtHalfPast.UpdatedAt.AddSeconds(30));

        var refusal = Assert.Throws<Refusal>(
            () => EntityTags.RequireCurrent(current, AtHalfPast, "The page"));

        Assert.Equal(RefusalCode.Stale, refusal.Code);
        Assert.True(refusal.Extensions.TryGetValue("updated_at", out var carried));
        Assert.Equal(current.UpdatedAt, carried);
    }

    [Fact]
    public void A_write_holding_the_current_version_passes()
    {
        EntityTags.RequireCurrent(AtHalfPast, ContentVersion.Of(AtHalfPast.UpdatedAt), "The page");
    }

    private static HttpRequest RequestSending(string ifMatch)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[EntityTags.IfMatch] = ifMatch;

        return request;
    }
}
