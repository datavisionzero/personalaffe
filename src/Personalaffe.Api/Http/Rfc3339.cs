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
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.TryParse(
            reader.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : throw new JsonException("A timestamp is RFC 3339.");

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
