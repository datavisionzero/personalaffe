using System.Net;
using Personalaffe.Application.Ports;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// The place the owner's files will go, checked before anything is served.
/// Nothing writes there yet — the Files application is PERSONAL-E6's — and the
/// failures this catches are an operator's, made once, at
/// <c>docker compose up</c>.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class StorageTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_root_that_is_not_there_yet_is_made_and_written_in()
    {
        var root = Path.Combine(Path.GetTempPath(), $"personalaffe-{Guid.NewGuid():n}", "files");

        await using var instance = AnInstance.Configured(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [StorageSettings.Variable] = root });

        using var client = instance.CreateClient();
        using var ready = await client.GetAsync("/api/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.True(Directory.Exists(root), $"{root} was not created.");

        // The probe is written and taken away again; a root full of leftovers
        // would be its own small mess.
        Assert.Empty(Directory.GetFileSystemEntries(root));

        Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
    }

    [Fact]
    public async Task A_root_that_cannot_be_written_to_stops_the_start()
    {
        // A file where a directory should be: the one unwritable root that can
        // be arranged the same way on every machine and under any user,
        // including the root that CI often runs as.
        var occupied = Path.Combine(Path.GetTempPath(), $"personalaffe-{Guid.NewGuid():n}.not-a-directory");
        await File.WriteAllTextAsync(occupied, "in the way", TestContext.Current.CancellationToken);

        await using var instance = AnInstance.Configured(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [StorageSettings.Variable] = occupied });

        await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains(
            instance.Warnings,
            warning => warning.Contains("cannot be written to", StringComparison.Ordinal)
                       && warning.Contains(StorageSettings.Variable, StringComparison.Ordinal));

        File.Delete(occupied);
    }

    [Fact]
    public async Task A_root_inside_the_static_web_root_stops_the_start()
    {
        // Files stored under wwwroot would be served to anyone who can guess a
        // name, which is a different product from the one VISION.md describes.
        //
        // The path is built rather than probed for on purpose: wwwroot does not
        // exist until somebody has run `npm run build`, and the first version of
        // this guard read it off the environment and so did nothing at all on a
        // checkout where nobody had. CI found that; the guard now asks for the
        // web root by name, and so does this test.
        var inside = Path.Combine(RepositoryRoot.Path, "src", "Personalaffe.Api", "wwwroot", "files");

        await using var instance = AnInstance.Configured(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [StorageSettings.Variable] = inside });

        await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains(
            instance.Warnings,
            warning => warning.Contains("static web root", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_is_in_the_storage_root_is_not_a_web_asset()
    {
        var root = Path.Combine(Path.GetTempPath(), $"personalaffe-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "private.txt"), "the owner's bytes", TestContext.Current.CancellationToken);

        await using var instance = AnInstance.Configured(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [StorageSettings.Variable] = root });

        using var client = instance.CreateClient();

        foreach (var address in (string[])["/private.txt", "/files/private.txt", "/assets/private.txt"])
        {
            using var response = await client.GetAsync(address, TestContext.Current.CancellationToken);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("the owner's bytes", body, StringComparison.Ordinal);
        }

        Directory.Delete(root, recursive: true);
    }
}
