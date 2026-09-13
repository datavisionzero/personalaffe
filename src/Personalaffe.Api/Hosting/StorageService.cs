using Personalaffe.Application.Ports;

namespace Personalaffe.Api.Hosting;

/// <summary>
/// Checks the file-storage root before the instance serves anything: that it
/// exists or can be made, that this process can write in it, and that it is not
/// somewhere the web server would hand out.
/// </summary>
/// <remarks>
/// <para>
/// Nothing writes files there yet — the Files application is PERSONAL-E6's. The
/// check is here because the failure it catches is an operator's, made once, at
/// `docker compose up`: a volume mounted read-only, a directory owned by root
/// with a container running as somebody else, a path inside the image that a
/// recreation throws away. Every one of those is invisible until the day
/// something is stored, and by then it is the owner's file that is missing.
/// </para>
/// <para>
/// A failed check stops the instance. Serving while the place the owner's files
/// go does not work is the wrong half of the trade: an instance that will not
/// start says so in the log an operator is already reading.
/// </para>
/// </remarks>
public sealed class StorageService(
    StorageSettings settings,
    IWebHostEnvironment environment,
    ILogger<StorageService> logger) : IHostedService
{
    /// <summary>
    /// Written and deleted to prove the root is writable. Named so that one left
    /// behind by a process that was killed mid-check is recognisable.
    /// </summary>
    private const string Probe = ".personalaffe-write-probe";

    /// <summary>Where the host serves static files from when there is a build to serve.</summary>
    private const string DefaultWebRoot = "wwwroot";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var root = settings.ResolvedRoot(environment.ContentRootPath);

        RefuseIfServedAsWebAssets(root);

        try
        {
            Directory.CreateDirectory(root);

            var probe = Path.Combine(root, Probe);
            await File.WriteAllTextAsync(probe, string.Empty, cancellationToken);
            File.Delete(probe);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogCritical(
                failure,
                "The file storage root {Root} cannot be written to. Set {Variable}, or give the volume to the "
                + "user this process runs as. The instance will not start.",
                root,
                StorageSettings.Variable);

            throw;
        }

        logger.LogInformation("File storage is {Root}.", root);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// A storage root under the static web root would be served to anyone who
    /// can guess a file name, which is a different product from the one
    /// <c>VISION.md</c> describes. It is refused here rather than left to the
    /// Files epic to notice.
    /// </summary>
    /// <remarks>
    /// The web root is asked for by name rather than taken from the environment
    /// alone, because a <c>wwwroot</c> that does not exist yet leaves
    /// <see cref="IWebHostEnvironment.WebRootPath"/> empty — a backend built
    /// without the web application beside it, which is every developer before
    /// their first <c>npm run build</c> and CI's own .NET jobs. The guard has to
    /// hold there too: what makes a path dangerous is where the static files
    /// will be served from, not whether anybody has built them yet.
    /// </remarks>
    private void RefuseIfServedAsWebAssets(string root)
    {
        var webRoot = Path.GetFullPath(
            string.IsNullOrEmpty(environment.WebRootPath)
                ? Path.Combine(environment.ContentRootPath, DefaultWebRoot)
                : environment.WebRootPath);
        var separator = Path.DirectorySeparatorChar;

        if (!root.TrimEnd(separator).StartsWith(webRoot.TrimEnd(separator) + separator, StringComparison.Ordinal)
            && root.TrimEnd(separator) != webRoot.TrimEnd(separator))
        {
            return;
        }

        var refusal = new InvalidOperationException(
            $"{StorageSettings.Variable} is {root}, which is inside the static web root {webRoot}. "
            + "Files stored there would be served to anyone who can guess a name. The instance will not start.");

        logger.LogCritical("{Reason}", refusal.Message);
        throw refusal;
    }
}
