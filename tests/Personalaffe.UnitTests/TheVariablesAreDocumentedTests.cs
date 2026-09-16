using System.Reflection;
using System.Text.RegularExpressions;
using Personalaffe.Api.Http;
using Personalaffe.Application.Ports;

namespace Personalaffe.UnitTests;

/// <summary>
/// The table in <c>docs/operations.md</c> is the one place an operator finds out
/// what this instance reads out of its environment, and a table maintained by
/// hand is a table that is wrong by the third epic that touches it. This walks
/// the code instead and fails the build when the two disagree.
/// </summary>
/// <remarks>
/// <para>
/// Four things are compared, and they are four because a variable can go missing
/// in four places: from the code, from the operator's document, from the example
/// file they copy, and from the Compose file that would have handed it to the
/// container. A variable the code reads and Compose never passes is one an
/// operator can set in <c>deploy/.env</c> all afternoon without effect.
/// </para>
/// <para>
/// This is deliberately textual. Nothing here parses YAML or Markdown properly,
/// because the point is not to understand those documents — it is to notice that
/// a name is in one of them and not in another, which a set difference does
/// perfectly well.
/// </para>
/// </remarks>
public sealed class TheVariablesAreDocumentedTests
{
    /// <summary>
    /// Every <c>const string …Variable</c> the settings types and
    /// <see cref="TrustedProxies"/> declare — which is every name the instance
    /// looks up, because <c>Program.cs</c> looks them up through these
    /// constants and never through a literal.
    /// </summary>
    private static readonly IReadOnlySet<string> Read = Variables();

    private static readonly string Operations =
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "docs", "operations.md"));

    private static readonly string Compose =
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "deploy", "docker-compose.yml"));

    private static readonly string Example =
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "deploy", ".env.example"));

    [Fact]
    public void Every_variable_is_read_through_its_constant_in_one_block()
    {
        var program = File.ReadAllText(
            Path.Combine(RepositoryRoot.Path, "src", "Personalaffe.Api", "Program.cs"));

        // Not that the name appears — that the constant is used. A literal in
        // Program.cs would pass a search for the name and defeat the whole of
        // this file.
        foreach (var (type, field) in Constants())
        {
            // The database is the one read through a different constant of the
            // same type: Configuration's GetConnectionString takes the name, and
            // Variable is that name with the ConnectionStrings__ prefix an
            // operator spells it with.
            var through = type == typeof(DatabaseSettings)
                ? $"{type.Name}.{nameof(DatabaseSettings.ConnectionStringName)}"
                : $"{type.Name}.{field.Name}";

            Assert.True(
                program.Contains(through, StringComparison.Ordinal),
                $"{type.Name}.{field.Name} is declared but Program.cs does not read it. "
                + "Everything read from the environment is read in one block, so that a value "
                + "the instance will not accept stops the start with one line.");
        }
    }

    [Fact]
    public void The_table_lists_every_variable_the_instance_reads()
    {
        Assert.Equal(Read.Order(StringComparer.Ordinal), Documented(columns: 4).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_second_table_is_the_two_that_only_compose_reads()
    {
        // Compose only, and the instance would not know what to do with either:
        // the port is the host's half of a published port, and the image is what
        // the port is published from.
        Assert.Equal(
            ["PERSONALAFFE_IMAGE", "PERSONALAFFE_PORT"],
            Documented(columns: 3).Order(StringComparer.Ordinal));

        Assert.Empty(Documented(columns: 3).Intersect(Read));
    }

    [Fact]
    public void Compose_hands_every_variable_the_instance_reads_to_the_container()
    {
        // The key of an entry under `environment:`, not a mention: half the
        // names in that file also appear in the comment above the entry, and a
        // check a comment satisfies is a check that survives the deletion of the
        // thing it was watching.
        var passed = Compose
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#') && line.Contains(':', StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf(':', StringComparison.Ordinal)])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var variable in Read)
        {
            Assert.True(
                passed.Contains(variable),
                $"{variable} is read by the instance and never appears in deploy/docker-compose.yml, "
                + "so an operator running it that way has no means of setting it.");
        }
    }

    [Fact]
    public void The_example_file_is_what_compose_lets_an_operator_set()
    {
        // What Compose substitutes is exactly what deploy/.env is for. A
        // variable Compose passes through and the example never mentions is one
        // an operator discovers by reading YAML.
        var substituted = Regex
            .Matches(Compose, @"\$\{(?<name>[A-Z][A-Z0-9_]*)[:?}-]")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var mentioned = Regex
            .Matches(Example, @"(?m)^#?\s*(?<name>[A-Z][A-Z0-9_]*)=")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            substituted.Order(StringComparer.Ordinal),
            mentioned.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The names in the first column of the table with <paramref name="columns"/>
    /// cells under <c>## What it reads</c>.
    /// </summary>
    private static IReadOnlySet<string> Documented(int columns)
    {
        var section = Operations.Split("## What it reads")[1].Split("\n## ")[0];

        return section
            .Split('\n')
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|', StringSplitOptions.RemoveEmptyEntries))
            .Where(cells => cells.Length == columns)
            .Select(cells => cells[0].Trim().Trim('`'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> Variables() =>
        Constants()
            .Select(constant => (string)constant.Field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(Type Type, FieldInfo Field)> Constants() =>
        new[] { typeof(StorageSettings).Assembly, typeof(TrustedProxies).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace is "Personalaffe.Application.Ports" or "Personalaffe.Api.Http")
            .SelectMany(type => type
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(field => field.IsLiteral
                                && field.FieldType == typeof(string)
                                && field.Name.EndsWith("Variable", StringComparison.Ordinal))
                .Select(field => (type, field)));
}
