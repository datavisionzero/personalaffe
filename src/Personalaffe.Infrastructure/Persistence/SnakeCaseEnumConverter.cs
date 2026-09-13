using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// An enum as the word the contract spells it: <c>ReadWrite</c> is
/// <c>read_write</c> in the column, in the API and in <c>docs/api.md</c>
/// (<c>docs/api.md</c>, Conventions).
/// </summary>
/// <remarks>
/// A number in the column was the alternative and loses on the one thing the
/// column is for: a check constraint that says
/// <c>scratchpad in ('none', 'read', 'read_write')</c> can be read, and one
/// that says <c>scratchpad in (0, 1, 2)</c> has to be looked up.
/// </remarks>
internal sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => ToName(value),
    name => FromName(name))
    where TEnum : struct, Enum
{
    private static readonly FrozenDictionary<TEnum, string> Names = Enum
        .GetValues<TEnum>()
        .ToFrozenDictionary(value => value, value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()));

    private static readonly FrozenDictionary<string, TEnum> Values =
        Names.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static string ToName(TEnum value) => Names[value];

    public static TEnum FromName(string name) =>
        Values.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(name), name, $"Not a {typeof(TEnum).Name}: the column holds a value the code does not know.");
}
