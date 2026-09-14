using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Personalaffe.Api.Http;

/// <summary>
/// Timestamps as <c>docs/api.md</c> spells them: RFC 3339 in UTC with
/// microseconds, <c>2026-09-13T14:03:07.123456Z</c>. One spelling, so that the
/// value a client reads in <c>updated_at</c> is the value it sends back when a
/// write has to say which version it is replacing.
/// </summary>
public sealed class Rfc3339 : JsonConverter<DateTimeOffset>
{
    /// <summary>
    /// The one spelling. It is not private because the guard on a write sends
    /// the same value back in a header, and a second format string somewhere
    /// else is how the two would come to disagree.
    /// </summary>
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>A moment, spelled the way every field of every response spells it.</summary>
    public static string Spell(DateTimeOffset moment) =>
        moment.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>
    /// The moment a spelling names, or nothing — for the places a malformed
    /// value is a refusal this code writes itself rather than one the JSON
    /// reader raises.
    /// </summary>
    public static DateTimeOffset? Moment(string? spelling) =>
        DateTimeOffset.TryParse(
            spelling,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.TryParse(
            reader.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : throw new JsonException("A timestamp is RFC 3339.");

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Spell(value));
}
