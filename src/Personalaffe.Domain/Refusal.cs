namespace Personalaffe.Domain;

/// <summary>
/// The product saying no: a <see cref="RefusalCode"/>, a sentence for a person,
/// and whatever the code needs beside it — the offending fields on
/// <c>validation</c>, the object's current state on <c>stale</c>.
/// </summary>
/// <remarks>
/// One type rather than one per code, because every adapter turns all of them
/// into the same document (<c>docs/api.md</c>, Errors) and the CLI into an exit
/// code, and neither wants a catch clause per rule. Extension members are keyed
/// the way the wire spells them, in <c>snake_case</c>, so that nothing between
/// here and the document has to rename them.
/// </remarks>
public sealed class Refusal(
    RefusalCode code,
    string detail,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(detail)
{
    public RefusalCode Code { get; } = code;

    public string Detail { get; } = detail;

    public IReadOnlyDictionary<string, object?> Extensions { get; } =
        extensions ?? new Dictionary<string, object?>();

    /// <summary>
    /// The <c>validation</c> refusal: which fields, and what is wrong with each.
    /// </summary>
    public static Refusal Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(
            RefusalCode.Validation,
            errors.Count == 1
                ? $"{errors.First().Key}: {string.Join("; ", errors.First().Value)}"
                : $"{errors.Count} fields are missing, malformed or over their limit.",
            new Dictionary<string, object?> { ["errors"] = errors });

    /// <inheritdoc cref="Validation(IReadOnlyDictionary{string, string[]})"/>
    public static Refusal Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>Nothing by that address.</summary>
    public static Refusal NotFound(string detail) => new(RefusalCode.NotFound, detail);

    /// <summary>
    /// What used to be there is in the Trash. It carries when it was deleted
    /// and when it stops being recoverable, so that a client can say how long
    /// is left without a second request — and so that "it is gone" and "it is
    /// gone for good" are not the same sentence.
    /// </summary>
    public static Refusal Deleted(string detail, DateTimeOffset deletedAt, DateTimeOffset expiresAt) =>
        new(
            RefusalCode.Deleted,
            detail,
            new Dictionary<string, object?> { ["deleted_at"] = deletedAt, ["expires_at"] = expiresAt });

    /// <summary>Something else already occupies that name or place.</summary>
    public static Refusal Conflict(string detail) => new(RefusalCode.Conflict, detail);

    /// <summary>
    /// What was sent is over a limit this instance sets on one thing. The limit
    /// travels with the refusal as <c>limit_bytes</c>, so that a client can say
    /// how much smaller without being told in a sentence it has to parse.
    /// </summary>
    public static Refusal TooLarge(string detail, long limitBytes) =>
        new(
            RefusalCode.TooLarge,
            detail,
            new Dictionary<string, object?> { ["limit_bytes"] = limitBytes });

    /// <summary>
    /// This instance has no room left. It carries what the whole of it may hold
    /// and what is already in it, because the owner's next move is to work out
    /// what to delete.
    /// </summary>
    public static Refusal OutOfSpace(string detail, long limitBytes, long usedBytes) =>
        new(
            RefusalCode.OutOfSpace,
            detail,
            new Dictionary<string, object?>
            {
                ["limit_bytes"] = limitBytes,
                ["used_bytes"] = usedBytes,
            });

    /// <summary>The caller may not do this.</summary>
    public static Refusal Forbidden(string detail) => new(RefusalCode.Forbidden, detail);

    public static Refusal Locked(string detail) => new(RefusalCode.Locked, detail);

    public static Refusal Throttled(string detail, DateTimeOffset retryAt, DateTimeOffset now) =>
        new(
            RefusalCode.Throttled,
            detail,
            new Dictionary<string, object?>
            {
                ["retry_at"] = retryAt,
                ["retry_after_seconds"] = Math.Max(1, (int)Math.Ceiling((retryAt - now).TotalSeconds)),
            });

    /// <summary>
    /// The write is holding a version that is no longer the object's — or is
    /// holding none at all, which the product treats the same way, because the
    /// client's move is the same either way: read it again and decide.
    /// </summary>
    /// <remarks>
    /// The object's current version travels with the refusal as
    /// <c>updated_at</c>, so that a client can tell "somebody changed this" from
    /// "I sent a malformed tag" without a second request.
    /// </remarks>
    public static Refusal Stale(string detail, ContentVersion? current = null) =>
        new(
            RefusalCode.Stale,
            detail,
            current is null
                ? null
                : new Dictionary<string, object?> { ["updated_at"] = current.UpdatedAt });
}
