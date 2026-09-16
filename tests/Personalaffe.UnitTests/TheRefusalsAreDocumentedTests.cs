using System.Globalization;
using Personalaffe.Api.Http;
using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// <c>docs/api.md</c> says of its table of codes: "the set is
/// <see cref="RefusalCode"/> and it grows with the epics that need it. Each
/// addition is a row in this table in the same commit." This is what makes that
/// sentence true rather than hopeful.
/// </summary>
/// <remarks>
/// The table is the only place a client author finds out what an instance can
/// answer, and a code that reaches the wire without a row is a code somebody
/// meets for the first time in production. Reading the document is cheaper than
/// remembering to write in it.
/// </remarks>
public sealed class TheRefusalsAreDocumentedTests
{
    private static readonly IReadOnlyDictionary<string, int> Documented = Rows();

    [Fact]
    public void Every_code_has_a_row_and_every_row_is_a_code()
    {
        Assert.Equal(
            Enum.GetValues<RefusalCode>().Select(Problems.CodeOf).Order(StringComparer.Ordinal),
            Documented.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_status_in_the_table_is_the_status_the_instance_answers()
    {
        foreach (var code in Enum.GetValues<RefusalCode>())
        {
            Assert.Equal(Problems.StatusOf(code), Documented[Problems.CodeOf(code)]);
        }
    }

    /// <summary>
    /// The first two columns of the table under <c>### The codes</c>: the wire
    /// spelling in a code span, and the status beside it.
    /// </summary>
    private static IReadOnlyDictionary<string, int> Rows()
    {
        var section = File
            .ReadAllText(Path.Combine(RepositoryRoot.Path, "docs", "api.md"))
            .Split("### The codes")[1]
            .Split("\n\n")[1];

        return section
            .Split('\n')
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(
                cells => cells[0].Trim().Trim('`'),
                cells => int.Parse(cells[1].Trim(), CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
    }
}
