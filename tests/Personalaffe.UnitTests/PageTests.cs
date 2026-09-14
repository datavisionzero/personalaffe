using System.Text;
using Personalaffe.Domain;
using Personalaffe.Domain.Knowledge;

namespace Personalaffe.UnitTests;

/// <summary>
/// The rules of a knowledge page and of its title: what an identity is, what a
/// label is, and what a change is.
/// </summary>
public sealed class PageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    /// <summary>
    /// A body with everything in it that a round trip breaks first: a fenced
    /// block, a table whose columns are spaces, trailing whitespace, and
    /// characters outside ASCII.
    /// </summary>
    private const string Body = "# Die Architektur\n\nZeile mit Leerzeichen am Ende   \n\n"
        + "| eins | zwei |\n| --- | --- |\n| ja | 日本語 🙂 |\n\n```sh\npea knowledge tree\n```\n";

    [Fact]
    public void A_page_keeps_the_markdown_it_was_written_with_byte_for_byte()
    {
        var page = Page.Written("Die Architektur", null, Body, Now);

        // Nothing is trimmed. A trailing newline is dropped from a Scratchpad
        // entry because a shell puts one there; a page's blank line at the end
        // is the person's.
        Assert.Equal(Body, page.Markdown);
        Assert.Equal(Encoding.UTF8.GetByteCount(Body), Encoding.UTF8.GetByteCount(page.Markdown));
    }

    [Fact]
    public void A_page_with_nothing_under_its_title_is_a_page()
    {
        // How a knowledge base usually starts one.
        var page = Page.Written("Noch nichts", null, null, Now);

        Assert.Equal(string.Empty, page.Markdown);
    }

    [Fact]
    public void Renaming_and_moving_leave_the_identity_alone()
    {
        var page = Page.Written("Die Architektur", null, Body, Now);
        var id = page.Id;
        var parent = Guid.CreateVersion7(Now);

        Assert.True(page.Write("Architecture", parent, Body, Later));

        // The whole promise of `CONTEXT.md`'s "its identity survives renaming
        // and moving": a link written down before this still names this page.
        Assert.Equal(id, page.Id);
        Assert.Equal("Architecture", page.Title);
        Assert.Equal(parent, page.ParentId);
        Assert.Equal(Later, page.UpdatedAt);
    }

    [Fact]
    public void A_write_that_asks_for_what_is_already_stored_is_not_a_change()
    {
        var page = Page.Written("Die Architektur", null, Body, Now);

        Assert.False(page.Write("Die Architektur", null, Body, Later));
        Assert.Equal(Now, page.UpdatedAt);
    }

    [Fact]
    public void Recovering_puts_a_title_and_a_body_back_together()
    {
        var page = Page.Written("Die Architektur", null, Body, Now);

        page.Write("Architecture", null, "something else", Later);
        page.Recover("Die Architektur", Body, Later.AddMinutes(1));

        // A history that kept only the Markdown would answer "what did this
        // say" and not "what was this called".
        Assert.Equal("Die Architektur", page.Title);
        Assert.Equal(Body, page.Markdown);
    }

    [Fact]
    public void A_revision_is_what_the_page_said_before_it_was_changed()
    {
        var page = Page.Written("Die Architektur", null, Body, Now);
        var (access, _) = AgentAccess.Grant("the writing agent", Permissions.Full, Now);

        var revision = PageRevision.Of(page, Caller.Agent(access), Later);

        page.Write("Architecture", null, "something else", Later);

        Assert.Equal(page.Id, revision.PageId);
        Assert.Equal("Die Architektur", revision.Title);
        Assert.Equal(Body, revision.Markdown);
        Assert.Equal(Later, revision.At);
        Assert.Equal("the writing agent", revision.By.Name);
    }

    [Fact]
    public void Markdown_over_the_limit_is_refused_and_the_message_names_it()
    {
        var tooMuch = new string('x', Page.MaxBytes + 1);

        var refusal = Assert.Throws<Refusal>(() => Page.Written("Zu viel", null, tooMuch, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains(
            Page.MaxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refusal.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_markdown_limit_is_bytes_of_utf8_and_not_characters()
    {
        // Every one of these is three bytes, so a third of the limit in
        // characters is the whole of it in bytes.
        var justOver = string.Concat(Enumerable.Repeat("あ", (Page.MaxBytes / 3) + 1));

        Assert.True(justOver.Length < Page.MaxBytes);
        Assert.Throws<Refusal>(() => Page.Written("Zu viel", null, justOver, Now));
    }

    [Fact]
    public void A_page_starts_out_of_the_trash_and_says_who_put_it_in()
    {
        var page = Page.Written("Die Architektur", null, Body, Now);
        var (access, _) = AgentAccess.Grant("the tidying agent", Permissions.Full, Now);

        Assert.False(page.IsDeleted());

        page.Delete(Caller.Agent(access), Later);

        Assert.True(page.IsDeleted());
        Assert.Equal("the tidying agent", page.DeletedBy?.Name);
    }

    [Fact]
    public void A_title_is_kept_as_it_was_typed_apart_from_the_space_around_it()
    {
        Assert.Equal("Die Architektur", PageTitle.Accepted("  Die Architektur  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_page_without_a_title_is_refused(string? asked)
    {
        var refusal = Assert.Throws<Refusal>(() => PageTitle.Accepted(asked));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains("title", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_title_is_at_most_two_hundred_characters_and_the_message_says_so()
    {
        var justFits = new string('x', PageTitle.MaxLength);

        Assert.Equal(justFits, PageTitle.Accepted(justFits));

        var refusal = Assert.Throws<Refusal>(() => PageTitle.Accepted(justFits + "x"));

        Assert.Contains("200", refusal.Detail, StringComparison.Ordinal);
        Assert.Contains("201", refusal.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Reisen/2026")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("zwei\nZeilen")]
    public void A_title_that_a_path_or_a_file_could_not_carry_is_refused(string asked)
    {
        // Two things outside the domain parse a title: `pea knowledge` walks a
        // path of them, and the export writes one file per page.
        Assert.Equal(RefusalCode.Validation, Assert.Throws<Refusal>(() => PageTitle.Accepted(asked)).Code);
    }

    [Fact]
    public void Everything_else_somebody_would_call_a_page_is_a_title()
    {
        foreach (var title in new[]
                 {
                     "Warum? Darum.", "C# und .NET", "Die »Architektur«", "1. Schritt", "日本語のページ",
                 })
        {
            Assert.Equal(title, PageTitle.Accepted(title));
        }
    }

    [Fact]
    public void Two_titles_that_differ_only_in_case_are_one_title()
    {
        Assert.True(PageTitle.Same("Architecture", "architecture"));
        Assert.False(PageTitle.Same("Architecture", "Architektur"));
    }
}
