using Personalaffe.Domain;

namespace Personalaffe.Api.Http;

/// <summary>
/// The guard on a write, in HTTP's words: a read answers <c>ETag</c> and a
/// write sends it back in <c>If-Match</c> (<c>docs/api.md</c>, The guarded
/// write).
/// </summary>
/// <remarks>
/// <para>
/// This is the only place that turns a <see cref="ContentVersion"/> into a tag
/// and a tag back into one, for the same reason <c>Problems</c> is the only
/// place that knows a refusal's status: the spelling is HTTP's business and the
/// product's vocabulary is <c>Personalaffe.Domain</c>'s.
/// </para>
/// <para>
/// <strong>A guarded write with no <c>If-Match</c> is refused</strong>, and so
/// is a weak tag, <c>*</c>, more than one tag, and anything that is not the
/// timestamp spelling <see cref="Rfc3339"/> writes. One code — <c>stale</c> —
/// for all of them, because the caller's move is the same in every case: read
/// the object again and decide what to do about what it says now. An endpoint
/// that let a write through without a tag would be an endpoint where forgetting
/// the guard is silently cheaper than using it.
/// </para>
/// <para>
/// The transport is a header and not a field in the body, because
/// PERSONAL-E6's uploads are writes whose body is the file itself. A guard only
/// half the writes in the product can use is not a guard.
/// </para>
/// </remarks>
public static class EntityTags
{
    /// <summary>The header a write sends its version in.</summary>
    public const string IfMatch = "If-Match";

    /// <summary>The header a read answers with.</summary>
    public const string ETag = "ETag";

    /// <summary>The strong entity tag for <paramref name="version"/>.</summary>
    public static string For(ContentVersion version) =>
        $"\"{Rfc3339.Spell(version.UpdatedAt)}\"";

    /// <summary>Puts the version of what is being answered on the response.</summary>
    public static void Write(HttpResponse response, ContentVersion version) =>
        response.Headers.ETag = For(version);

    /// <summary>
    /// The version this write says it is replacing.
    /// </summary>
    /// <exception cref="Refusal">
    /// <c>stale</c>: there is no usable tag on the request.
    /// </exception>
    public static ContentVersion Required(HttpRequest request)
    {
        var sent = request.Headers[IfMatch];

        if (sent.Count == 0)
        {
            throw Refusal.Stale(
                $"This write did not say which version it replaces. Read the object, then send its "
                + $"{ETag} back in {IfMatch}.");
        }

        if (sent.Count > 1)
        {
            throw Refusal.Stale(
                $"{IfMatch} names more than one version. A write replaces one version of one object.");
        }

        var tag = sent[0]?.Trim() ?? string.Empty;

        // `*` means "whatever is there now", which is the overwrite this
        // product does not offer, and a weak tag means "near enough", which is
        // not a thing a version can be.
        if (tag == "*")
        {
            throw Refusal.Stale(
                $"{IfMatch}: `*` would replace whatever is there now. Send the version this write read.");
        }

        if (tag.StartsWith("W/", StringComparison.Ordinal))
        {
            throw Refusal.Stale($"{IfMatch}: a version is a strong entity tag, not a weak one.");
        }

        var quoted = tag.Length >= 2 && tag[0] == '"' && tag[^1] == '"';

        if (!quoted || Rfc3339.Moment(tag[1..^1]) is not { } moment)
        {
            throw Refusal.Stale(
                $"{IfMatch} is not a version this instance wrote. It is the {ETag} of the read this "
                + "write is based on, quotation marks included.");
        }

        return ContentVersion.Of(moment);
    }

    /// <summary>
    /// The object's version, or the refusal saying the caller is holding an
    /// older one — the check every guarded write makes before it changes
    /// anything.
    /// </summary>
    /// <exception cref="Refusal"><c>stale</c>: somebody else got there first.</exception>
    public static void RequireCurrent(ContentVersion current, ContentVersion held, string what)
    {
        if (!current.Matches(held))
        {
            throw Refusal.Stale(
                $"{what} has changed since it was read. Read it again: the write you sent would have "
                + "replaced somebody else's newer one.",
                current);
        }
    }
}
