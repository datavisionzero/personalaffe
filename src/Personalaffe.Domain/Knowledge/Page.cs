using System.Globalization;
using System.Text;

namespace Personalaffe.Domain.Knowledge;

/// <summary>
/// A lasting piece of personal knowledge with a title and a place in the page
/// hierarchy; its identity survives renaming and moving (<c>CONTEXT.md</c>,
/// Knowledge page).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The id is the identity, and nothing else is.</strong> The title is a
/// label and the parent is a place, and the owner changes both as often as they
/// like; a link written down a year ago keeps working through every one of
/// those changes. That sentence is `CONTEXT.md`'s own — "its identity survives
/// renaming and moving" — and this is where it is true.
/// </para>
/// <para>
/// <strong>It is recoverable</strong> (<see cref="IRecoverable"/>) and it keeps
/// history (<see cref="Revisions"/>). It is the first module in this product to
/// do both, and the second to be a tree — Files was the first, and what the two
/// share is in <see cref="Restoration"/> rather than in either of them.
/// </para>
/// <para>
/// <strong>What is stored is Markdown source.</strong> Nothing here parses it,
/// renders it, or has an opinion about it; what the owner typed is what comes
/// back. The renderer is the browser's and is where safety is decided
/// (<c>src/web/src/shared/Markdown.tsx</c>), because the same body has to be
/// safe to show whether an agent or the owner wrote it.
/// </para>
/// </remarks>
public sealed class Page : IRecoverable
{
    /// <summary>
    /// How deep the hierarchy goes: a page at the top is at 1.
    /// </summary>
    /// <remarks>
    /// VISION §14.1 leaves the number here, and it is deliberately not Files'
    /// thirty-two. A file tree mirrors however somebody already filed things on
    /// a disk; a knowledge base is something they are building to find things
    /// in, and eight levels of it is a base where nothing is findable. The limit
    /// is low enough to be felt, which is the point.
    /// </remarks>
    public const int MaxDepth = 8;

    /// <summary>
    /// How much Markdown one page holds, in bytes of UTF-8.
    /// </summary>
    /// <remarks>
    /// A mebibyte is roughly a novel. Nobody reaches it by writing a page, and
    /// anything that does reach it is a page that wanted to be several — or
    /// something pasted in that belongs in Files. Bytes rather than characters,
    /// for the reason <c>ScratchpadEntry.MaxBytes</c> gives.
    /// </remarks>
    public const int MaxBytes = 1024 * 1024;

    private Page()
    {
    }

    /// <summary>Made when the page is written, and never again.</summary>
    public Guid Id { get; private init; }

    /// <summary>What the owner calls it (<see cref="PageTitle"/>).</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The page it is under, or nothing for one at the top.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>The Markdown source, as it was written.</summary>
    public string Markdown { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When it was last changed, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Actor? DeletedBy { get; set; }

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>A page written now.</summary>
    /// <exception cref="Refusal"><c>validation</c>: not a title, or too much Markdown.</exception>
    public static Page Written(string? title, Guid? parent, string? markdown, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Title = PageTitle.Accepted(title),
        ParentId = parent,
        Markdown = Accepted(markdown),
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Changes the title, the place, the Markdown, or any of them, and says
    /// whether that changed anything.
    /// </summary>
    /// <remarks>
    /// <strong>One write and not three.</strong> A rename, a move and an edit
    /// are all changes to the same row, and three addresses would be three
    /// places the guard has to be got right — and three revisions where the
    /// owner made one change. It is the shape the Scratchpad's one <c>PUT</c>
    /// established and Files kept.
    /// </remarks>
    /// <exception cref="Refusal"><c>validation</c>: not a title, or too much Markdown.</exception>
    public bool Write(string? title, Guid? parent, string? markdown, DateTimeOffset now)
    {
        var wantedTitle = PageTitle.Accepted(title);
        var wantedMarkdown = Accepted(markdown);

        if (string.Equals(Title, wantedTitle, StringComparison.Ordinal)
            && ParentId == parent
            && string.Equals(Markdown, wantedMarkdown, StringComparison.Ordinal))
        {
            return false;
        }

        Title = wantedTitle;
        ParentId = parent;
        Markdown = wantedMarkdown;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Puts an earlier title and body back, which is a change like any other.
    /// </summary>
    /// <remarks>
    /// It is spelled apart from <see cref="Write"/> because what it takes has
    /// already been through the rules once — it was stored — and because the act
    /// that calls it is the one that has to leave a revision of what was current
    /// until now. <strong>Recovering writes forward</strong>
    /// (<see cref="Revisions"/>): history only grows, and the version this
    /// replaces is itself recoverable a moment later.
    /// </remarks>
    public void Recover(string title, string markdown, DateTimeOffset now)
    {
        Title = title;
        Markdown = markdown;
        UpdatedAt = now;
    }

    /// <summary>Moves the version on for a change the module made itself.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>
    /// The Markdown as it is stored: whatever was written, held to the one
    /// limit.
    /// </summary>
    /// <remarks>
    /// <strong>Nothing is trimmed.</strong> A trailing newline is dropped from a
    /// Scratchpad entry because a shell puts one there; a page is written in a
    /// field or read out of a file somebody keeps, and the blank line at the end
    /// is theirs. An empty page is allowed too: a title with nothing under it
    /// yet is how a knowledge base usually starts a page.
    /// </remarks>
    private static string Accepted(string? markdown)
    {
        var kept = markdown ?? string.Empty;
        var bytes = Encoding.UTF8.GetByteCount(kept);

        return bytes <= MaxBytes
            ? kept
            : throw Refusal.Validation(
                "markdown",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A page is at most {MaxBytes} bytes of UTF-8, and this one is {bytes}. "
                    + $"What does not fit is more than one page, or a file."));
    }
}
