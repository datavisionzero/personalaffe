using System.Xml.Linq;

namespace Personalaffe.UnitTests;

/// <summary>
/// <c>docs/codebase.md</c> has the dependencies point inward and only inward,
/// and calls Domain's emptiness the cheapest check that nothing has leaked into
/// it. A convention nobody can run is not a check, so this reads the project
/// files and turns the layering into a failing build.
/// </summary>
public sealed class LayeringTests
{
    [Fact]
    public void Domain_depends_on_nothing_at_all()
    {
        var domain = Layer.Read("Personalaffe.Domain");

        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.PackageReferences);
    }

    [Fact]
    public void Application_depends_on_domain_only()
    {
        Assert.Equal(["Personalaffe.Domain"], Layer.Read("Personalaffe.Application").ProjectReferences);
    }

    [Fact]
    public void Infrastructure_depends_on_application_only()
    {
        Assert.Equal(
            ["Personalaffe.Application"],
            Layer.Read("Personalaffe.Infrastructure").ProjectReferences);
    }

    [Fact]
    public void Api_is_the_composition_root_and_depends_on_the_two_outer_layers()
    {
        Assert.Equal(
            ["Personalaffe.Application", "Personalaffe.Infrastructure"],
            Layer.Read("Personalaffe.Api").ProjectReferences);
    }

    private sealed record Layer(
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> PackageReferences)
    {
        public static Layer Read(string project)
        {
            var document = XDocument.Load(
                Path.Combine(RepositoryRoot.Path, "src", project, $"{project}.csproj"));

            return new Layer(References(document, "ProjectReference"), References(document, "PackageReference"));
        }

        private static IReadOnlyList<string> References(XDocument project, string element) =>
            [.. project.Descendants(element)
                .Select(reference => Path.GetFileNameWithoutExtension(
                    reference.Attribute("Include")?.Value ?? string.Empty))
                .Order(StringComparer.Ordinal)];
    }
}
