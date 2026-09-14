using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Scratchpad;

namespace Personalaffe.Application.Acts.Scratchpad;

/// <summary>
/// One Scratchpad entry, as a caller sees it: the text, the pin, and when the
/// instance will destroy it.
/// </summary>
/// <remarks>
/// <see cref="ExpiresAt"/> is worked out here, from the instance's retention,
/// and never by a client or an endpoint. It is nothing at all while the entry is
/// pinned, which is what "pinning is the answer to keep this" means on the wire.
/// </remarks>
public sealed record TheEntry(
    Guid Id,
    string Text,
    bool Pinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    internal static TheEntry Of(ScratchpadEntry entry, TimeSpan retention) => new(
        entry.Id,
        entry.Text,
        entry.Pinned,
        entry.CreatedAt,
        entry.UpdatedAt,
        entry.ExpiresAt(retention));
}

/// <summary>A page of the Scratchpad, and whether the limit cut it short.</summary>
public sealed record TheScratchpad(IReadOnlyList<TheEntry> Items, bool HasMore);

/// <summary>Text put down in seconds.</summary>
public sealed class CaptureAnEntry(
    ReachingAnApplication reaching,
    IScratchpadEntries entries,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task<TheEntry> ExecuteAsync(string? text, bool pinned, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Scratchpad, cancellationToken);

        var entry = ScratchpadEntry.Capture(text, pinned, clock.GetUtcNow());

        await entries.AddAsync(entry, cancellationToken);

        return TheEntry.Of(entry, retention.Scratchpad);
    }
}

/// <summary>
/// What is in the Scratchpad, newest capture first.
/// </summary>
/// <remarks>
/// <strong>There is no cursor</strong>, for the reason the Trash already gives:
/// a personal Scratchpad holds one person's notes over one retention period, and
/// a limit with <c>has_more</c> is what keeps a runaway from becoming an
/// unbounded response. Each item carries its own version and its expiry, so the
/// list alone is enough to pin or delete from without a second read.
/// </remarks>
public sealed class ReadTheEntries(
    ReachingAnApplication reaching, IScratchpadEntries entries, RetentionSettings retention)
{
    public const int DefaultLimit = 200;

    public const int MaxLimit = 1000;

    public async Task<TheScratchpad> ExecuteAsync(int? limit, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Scratchpad, cancellationToken);

        var wanted = Limit(limit);

        // One more than asked for, so that "there is more" is a fact rather
        // than a guess about whether the limit cut something off.
        var found = await entries.ListAsync(wanted + 1, cancellationToken);

        return new TheScratchpad(
            [.. found.Take(wanted).Select(entry => TheEntry.Of(entry, retention.Scratchpad))],
            found.Count > wanted);
    }

    private static int Limit(int? asked) => asked switch
    {
        null => DefaultLimit,
        < 1 => throw Refusal.Validation("limit", "A limit is at least 1."),
        > MaxLimit => throw Refusal.Validation("limit", $"A limit is at most {MaxLimit}."),
        _ => asked.Value,
    };
}

/// <summary>One entry, by its own address.</summary>
public sealed class ReadAnEntry(
    ReachingAnApplication reaching, IScratchpadEntries entries, RetentionSettings retention)
{
    public async Task<TheEntry> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        await reaching.ToReadAsync(WorkspaceApplication.Scratchpad, cancellationToken);

        var entry = await entries.FindAsync(id, cancellationToken) ?? throw Scratchpad.NoSuchEntry(id);

        return TheEntry.Of(entry, retention.Scratchpad);
    }
}

/// <summary>
/// Changes an entry's text, its pin, or both.
/// </summary>
/// <remarks>
/// <strong>One write and not two.</strong> A pin is a change to the entry and
/// not an event of its own, and a second address carrying one boolean would be a
/// second place the guard has to be got right. A write that asks for what is
/// already stored changes nothing and does not move the version — the guard is
/// still checked, because a write that agreed with what is there is still a
/// write somebody made from a stale screen.
/// </remarks>
public sealed class RewriteAnEntry(
    ReachingAnApplication reaching,
    IScratchpadEntries entries,
    RetentionSettings retention,
    TimeProvider clock)
{
    public async Task<TheEntry> ExecuteAsync(
        Guid id, string? text, bool pinned, ContentVersion held, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Scratchpad, cancellationToken);

        var entry = await entries.FindAsync(id, cancellationToken) ?? throw Scratchpad.NoSuchEntry(id);

        Scratchpad.RequireCurrent(entry, held);

        var now = clock.GetUtcNow();
        var rewritten = entry.Rewrite(text, now);
        var pinning = pinned ? entry.Pin(now) : entry.Unpin(now);

        if (rewritten || pinning)
        {
            await entries.SaveAsync(cancellationToken);
        }

        return TheEntry.Of(entry, retention.Scratchpad);
    }
}

/// <summary>
/// Destroys one entry.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no way back from this</strong>, and that is the design
/// rather than an omission (<c>docs/api.md</c>, Deleting sets content aside). A
/// Scratchpad entry is temporary by definition, an owner who deletes one means
/// it, and a Trash full of the text somebody pasted between two devices is a
/// service to nobody.
/// </para>
/// <para>
/// <strong>Write access is enough, and an agent has it.</strong> This is the one
/// destruction in this product agent access may make: PERSONAL-E2 said so —
/// <c>read_write</c> includes deletion, which for the Scratchpad is permanent —
/// and what is protected from an agent is lasting content, which this is not.
/// </para>
/// </remarks>
public sealed class DiscardAnEntry(ReachingAnApplication reaching, IScratchpadEntries entries)
{
    public async Task ExecuteAsync(Guid id, ContentVersion held, CancellationToken cancellationToken)
    {
        await reaching.ToWriteAsync(WorkspaceApplication.Scratchpad, cancellationToken);

        var entry = await entries.FindAsync(id, cancellationToken) ?? throw Scratchpad.NoSuchEntry(id);

        Scratchpad.RequireCurrent(entry, held);

        await entries.RemoveAsync(entry, cancellationToken);
    }
}

/// <summary>What the acts above have in common, and nothing more.</summary>
internal static class Scratchpad
{
    /// <summary>
    /// The one sentence for an entry that is not there.
    /// </summary>
    /// <remarks>
    /// <c>not-found</c> and never <c>deleted</c>: <c>deleted</c> means the owner
    /// can have the thing back, and in this application nobody ever can. A code
    /// this application never answers is a code no client of it has to handle.
    /// </remarks>
    internal static Refusal NoSuchEntry(Guid id) => Refusal.NotFound(
        $"Nothing with the id {id} is in the Scratchpad. A Scratchpad entry is destroyed when it is "
        + "deleted and when it expires, and there is no way back from either.");

    internal static void RequireCurrent(ScratchpadEntry entry, ContentVersion held)
    {
        if (!entry.Version.Matches(held))
        {
            throw Refusal.Stale(
                "The entry has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.",
                entry.Version);
        }
    }
}
