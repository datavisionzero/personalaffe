using System.Text;
using Personalaffe.Domain;
using Personalaffe.Domain.Scratchpad;

namespace Personalaffe.UnitTests;

/// <summary>
/// The rules of a Scratchpad entry: what text is accepted, what a trailing
/// newline is, and the clock it disappears by.
/// </summary>
public sealed class ScratchpadEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    [Fact]
    public void Text_survives_capture_byte_for_byte()
    {
        // What breaks first in a piece of somebody's text: characters outside
        // ASCII, an emoji that is more than one code unit, a tab, and the
        // newlines in the middle.
        const string written = "Größe: 5 m²\n\n\tZeile zwei — 日本語 🙂\n\tund ein Tabulator";

        var entry = ScratchpadEntry.Capture(written, pinned: false, Now);

        Assert.Equal(written, entry.Text);
    }

    [Theory]
    [InlineData("a note\n", "a note")]
    [InlineData("a note\r\n", "a note")]
    [InlineData("a note\n\n", "a note\n")]
    [InlineData("a note", "a note")]
    [InlineData("\na note\n", "\na note")]
    public void Exactly_one_trailing_newline_is_dropped_and_never_two(string written, string kept)
    {
        // A trailing newline is how a shell ends a line; a second one is
        // somebody's blank line and stays.
        Assert.Equal(kept, ScratchpadEntry.Capture(written, pinned: false, Now).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("   ")]
    [InlineData(" \t \n")]
    public void Nothing_and_whitespace_alone_are_refused_by_name(string? written)
    {
        var refusal = Assert.Throws<Refusal>(() => ScratchpadEntry.Capture(written, pinned: false, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains("text", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_over_the_limit_is_refused_and_the_message_names_the_limit()
    {
        var tooMuch = new string('x', ScratchpadEntry.MaxBytes + 1);

        var refusal = Assert.Throws<Refusal>(() => ScratchpadEntry.Capture(tooMuch, pinned: false, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains(ScratchpadEntry.MaxBytes.ToString(), refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_limit_is_bytes_of_utf8_and_not_characters()
    {
        // Three bytes each, so a third of the limit in characters is the whole
        // of it in bytes — which is the difference the column and the request
        // body actually feel.
        var japanese = new string('あ', ScratchpadEntry.MaxBytes / 3);

        Assert.Equal(japanese.Length * 3, Encoding.UTF8.GetByteCount(japanese));
        Assert.InRange(Encoding.UTF8.GetByteCount(japanese), ScratchpadEntry.MaxBytes - 2, ScratchpadEntry.MaxBytes);
        Assert.Equal(japanese, ScratchpadEntry.Capture(japanese, pinned: false, Now).Text);

        Assert.Throws<Refusal>(() => ScratchpadEntry.Capture(japanese + "あ", pinned: false, Now));
    }

    [Fact]
    public void An_entry_that_is_not_pinned_expires_a_period_after_it_was_last_changed()
    {
        var entry = ScratchpadEntry.Capture("a note", pinned: false, Now);

        Assert.Equal(Now + Week, entry.ExpiresAt(Week));

        // Edited this morning, so it does not go tonight because it was pasted
        // a week ago: the period counts from the change and not from the
        // capture.
        var later = Now.AddDays(5);
        Assert.True(entry.Rewrite("a different note", later));

        Assert.Equal(later + Week, entry.ExpiresAt(Week));
        Assert.Equal(Now, entry.CreatedAt);
    }

    [Fact]
    public void A_pinned_entry_has_no_expiry_at_all()
    {
        var entry = ScratchpadEntry.Capture("a note", pinned: true, Now);

        Assert.Null(entry.ExpiresAt(Week));
        Assert.Null(entry.ExpiresAt(TimeSpan.FromDays(1)));
    }

    [Fact]
    public void Unpinning_gives_a_full_period_from_that_moment()
    {
        var entry = ScratchpadEntry.Capture("a note", pinned: true, Now);

        // Pinned for a year, and then let go. The answer this epic settled is
        // that it gets a whole period from the unpinning rather than being
        // destroyed by the next sweep.
        var muchLater = Now.AddDays(365);

        Assert.True(entry.Unpin(muchLater));
        Assert.Equal(muchLater + Week, entry.ExpiresAt(Week));
    }

    [Fact]
    public void Pinning_and_unpinning_move_the_version_only_when_they_change_something()
    {
        var entry = ScratchpadEntry.Capture("a note", pinned: false, Now);

        Assert.False(entry.Unpin(Now.AddHours(1)));
        Assert.Equal(Now, entry.UpdatedAt);

        Assert.True(entry.Pin(Now.AddHours(2)));
        Assert.Equal(Now.AddHours(2), entry.UpdatedAt);
        Assert.True(entry.Pinned);

        Assert.False(entry.Pin(Now.AddHours(3)));
        Assert.Equal(Now.AddHours(2), entry.UpdatedAt);
    }

    [Fact]
    public void Rewriting_with_the_same_text_is_not_a_change()
    {
        var entry = ScratchpadEntry.Capture("a note", pinned: false, Now);

        // The same text, with the trailing newline a shell would add: the same
        // stored text, so nothing moved.
        Assert.False(entry.Rewrite("a note\n", Now.AddHours(1)));
        Assert.Equal(Now, entry.UpdatedAt);

        Assert.True(entry.Rewrite("another note", Now.AddHours(2)));
        Assert.Equal(Now.AddHours(2), entry.UpdatedAt);
        Assert.Equal("another note", entry.Text);
    }

    [Fact]
    public void Rewriting_with_nothing_refuses_and_leaves_the_text_alone()
    {
        var entry = ScratchpadEntry.Capture("a note", pinned: false, Now);

        Assert.Throws<Refusal>(() => entry.Rewrite("   ", Now.AddHours(1)));

        Assert.Equal("a note", entry.Text);
        Assert.Equal(Now, entry.UpdatedAt);
    }

    [Fact]
    public void The_version_is_the_moment_it_was_last_changed()
    {
        var entry = ScratchpadEntry.Capture("a note", pinned: false, Now);

        Assert.Equal(ContentVersion.Of(entry.UpdatedAt), entry.Version);
        Assert.True(entry.Version.Matches(ContentVersion.Of(Now)));
    }

    [Fact]
    public void An_id_is_made_at_the_moment_of_capture()
    {
        var first = ScratchpadEntry.Capture("a note", pinned: false, Now);
        var second = ScratchpadEntry.Capture("a note", pinned: false, Now.AddSeconds(1));

        Assert.NotEqual(first.Id, second.Id);

        // Version 7, as every other id in this product is: the time is in it,
        // so the order of two ids is the order they were made in.
        Assert.Equal(7, first.Id.Version);
        Assert.True(second.Id.CompareTo(first.Id) > 0);
    }

    [Fact]
    public void A_scratchpad_entry_is_deliberately_not_recoverable()
    {
        // The one application that inherits the guard and not the Trash. If
        // somebody makes this type recoverable by habit, the deletion stops
        // meaning what docs/api.md says it means.
        Assert.False(typeof(IRecoverable).IsAssignableFrom(typeof(ScratchpadEntry)));
    }
}
