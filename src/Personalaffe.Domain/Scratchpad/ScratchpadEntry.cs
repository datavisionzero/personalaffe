using System.Text;

namespace Personalaffe.Domain.Scratchpad;

/// <summary>
/// A temporary piece of plain text kept for cross-device use
/// (<c>CONTEXT.md</c>, Scratchpad entry).
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is not recoverable and it is not in the Trash.</strong> Every
/// other piece of content in this workspace is set aside when it is deleted
/// (<c>docs/api.md</c>, Deleting sets content aside); this one is destroyed,
/// and the table carries no <c>deleted_at</c> for a sweep or a list to find. An
/// owner who deletes something they pasted between two devices means it, and a
/// Trash full of that is a service to nobody. So this type deliberately does not
/// implement <see cref="IRecoverable"/>, and the module registers no
/// <c>ITrash</c>: it is the one application that inherits PERSONAL-E3's guard
/// and not its Trash.
/// </para>
/// <para>
/// <strong>Plain text, and nothing around it.</strong> No title, no tags, no
/// Markdown, no folder — VISION.md §6.2 is explicit, and every one of those
/// would be the beginning of a second Knowledge.
/// </para>
/// </remarks>
public sealed class ScratchpadEntry
{
    /// <summary>
    /// How much text one entry holds, in bytes of UTF-8.
    /// </summary>
    /// <remarks>
    /// 64 KiB is far more than anybody pastes between two devices and far less
    /// than a row somebody has to be told about. The limit is in bytes rather
    /// than characters because bytes are what the column and the request body
    /// are measured in, and a limit that let one emoji count as one character
    /// would be a limit that means something different in German and in
    /// Japanese.
    /// </remarks>
    public const int MaxBytes = 64 * 1024;

    private ScratchpadEntry()
    {
    }

    /// <summary>Made at the moment of capture, as every other id here is.</summary>
    public Guid Id { get; private init; }

    /// <summary>What the owner or an agent put down. Never empty.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>Whether automatic expiry passes this one by.</summary>
    public bool Pinned { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When it was last changed, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>Text put down now.</summary>
    /// <exception cref="Refusal"><c>validation</c>: nothing, or too much.</exception>
    public static ScratchpadEntry Capture(string? text, bool pinned, DateTimeOffset now)
    {
        var kept = Accepted(text);

        return new ScratchpadEntry
        {
            Id = Guid.CreateVersion7(now),
            Text = kept,
            Pinned = pinned,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the text, and says whether that changed anything — text that is
    /// what is already stored is not a change and does not move the version.
    /// </summary>
    /// <exception cref="Refusal"><c>validation</c>: nothing, or too much.</exception>
    public bool Rewrite(string? text, DateTimeOffset now)
    {
        var kept = Accepted(text);

        if (string.Equals(Text, kept, StringComparison.Ordinal))
        {
            return false;
        }

        Text = kept;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Exempts it from expiry, and says whether that changed anything.</summary>
    public bool Pin(DateTimeOffset now) => Pinning(true, now);

    /// <summary>
    /// Puts it back under the clock, and says whether that changed anything.
    /// </summary>
    /// <remarks>
    /// Unpinning moves <see cref="UpdatedAt"/>, which is what gives the entry a
    /// full period from this moment rather than from a capture a year ago. That
    /// is the whole of the answer to "what happens after unpinning": it does not
    /// vanish on the next sweep.
    /// </remarks>
    public bool Unpin(DateTimeOffset now) => Pinning(false, now);

    /// <summary>
    /// When the instance's own sweep will destroy this one, or nothing at all
    /// while it is pinned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It counts from <see cref="UpdatedAt"/> and not from
    /// <see cref="CreatedAt"/>.</strong> An entry somebody edited this morning
    /// does not disappear tonight because it was pasted a week ago, and an entry
    /// unpinned after a year gets a full period from the moment it was unpinned.
    /// The retention itself is the instance's and never the entry's: it arrives
    /// as an argument so that nothing here can come to its own conclusion about
    /// how long a period is.
    /// </para>
    /// </remarks>
    public DateTimeOffset? ExpiresAt(TimeSpan retention) => Pinned ? null : UpdatedAt + retention;

    private bool Pinning(bool pinned, DateTimeOffset now)
    {
        if (Pinned == pinned)
        {
            return false;
        }

        Pinned = pinned;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// The text as it is stored: one trailing newline dropped, and whatever is
    /// left held to the two rules.
    /// </summary>
    private static string Accepted(string? text)
    {
        var kept = WithoutOneTrailingNewline(text ?? string.Empty);

        if (string.IsNullOrWhiteSpace(kept))
        {
            throw Refusal.Validation(
                "text", "A Scratchpad entry is a piece of text. Whitespace on its own is not one.");
        }

        var bytes = Encoding.UTF8.GetByteCount(kept);

        if (bytes > MaxBytes)
        {
            throw Refusal.Validation(
                "text",
                $"A Scratchpad entry is at most {MaxBytes} bytes of UTF-8, and this one is {bytes}. "
                + "What does not fit belongs in a knowledge page or a file.");
        }

        return kept;
    }

    /// <summary>
    /// One trailing newline gone, and never two.
    /// </summary>
    /// <remarks>
    /// A trailing newline is how a shell ends a line — <c>echo</c> writes one,
    /// an editor saving a file writes one — and not something the person typed.
    /// The newlines inside are theirs and stay, and so is a second trailing one:
    /// somebody who left a blank line at the end left it on purpose, or at least
    /// left it, and this is not the place to decide otherwise.
    /// </remarks>
    private static string WithoutOneTrailingNewline(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return text.EndsWith('\n') ? text[..^1] : text;
    }
}
