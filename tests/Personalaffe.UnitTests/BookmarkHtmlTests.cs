using Personalaffe.Domain;
using Personalaffe.Domain.Bookmarks;
using Personalaffe.Infrastructure.Bookmarks;

namespace Personalaffe.UnitTests;

public sealed class BookmarkHtmlTests
{
    [Fact]
    public void Browser_export_parses_nested_folders_unicode_entities_and_descriptions_without_execution()
    {
        var parsed = new BookmarkHtml().Parse("""
            <!DOCTYPE NETSCAPE-Bookmark-file-1>
            <TITLE>Bookmarks</TITLE><H1>Bookmarks</H1><DL><p>
              <DT><H3>旅行 &amp; Notes</H3><DL><p>
                <DT><A HREF="https://example.com/?a=1&amp;b=2">Größe &lt;Guide&gt;</A>
                <DD>A description &amp; explanation
                <DT><H3>Nested</H3><DL><p><DT><A HREF="https://example.org">Another</A></DL><p>
              </DL><p>
              <DT><A HREF="javascript:alert(1)">Unsafe</A>
              <SCRIPT>fetch('https://outside.invalid')</SCRIPT><IMG SRC="https://outside.invalid/pixel">
            </DL><p>
            """);
        Assert.Equal(4, parsed.Nodes.Count);
        Assert.Single(parsed.Rejected);
        Assert.Equal("旅行 & Notes", parsed.Nodes[0].Title);
        Assert.Equal(0, parsed.Nodes[1].Parent);
        Assert.Equal("https://example.com/?a=1&b=2", parsed.Nodes[1].Url);
        Assert.Contains("description & explanation", parsed.Nodes[1].Description);
        Assert.Equal(2, parsed.Nodes[3].Parent);
    }

    [Fact]
    public void Invalid_folder_does_not_silently_flatten_its_contents_and_limits_are_explicit()
    {
        var parsed = new BookmarkHtml().Parse("<DL><DT><H3></H3><DL><DT><A HREF='https://example.com'>Inside</A></DL></DL>");
        Assert.Empty(parsed.Nodes); Assert.Equal(2, parsed.Rejected.Count);
        Assert.Throws<Refusal>(() => new BookmarkHtml().Parse(new string('x', 2 * 1024 * 1024 + 1)));
        Assert.Throws<Refusal>(() => new BookmarkHtml().Parse(string.Concat(Enumerable.Repeat("<A HREF='https://example.com'>Link</A>", 5001))));
        Assert.Throws<Refusal>(() => new BookmarkHtml().Parse(string.Concat(Enumerable.Repeat("<DL><DT><H3>Folder</H3>", 34)) + "<DL><A HREF='https://example.com'>Link</A>"));
    }

    [Theory]
    [InlineData("HTTPS://EXAMPLE.com:443/a?b=1#part", "https://example.com/a?b=1#part")]
    [InlineData("http://Example.com:80", "http://example.com")]
    [InlineData("https://example.com/a/../b", "https://example.com/a/../b")]
    [InlineData("https://example.com/%7e?x=1&y=2#Case", "https://example.com/%7e?x=1&y=2#Case")]
    public void Duplicate_key_only_normalizes_authority(string input, string expected) => Assert.Equal(expected, BookmarkUrl.Key(input));
}
