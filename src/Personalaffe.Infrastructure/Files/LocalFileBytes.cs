using Microsoft.Extensions.Logging;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;
using Personalaffe.Domain.Files;

namespace Personalaffe.Infrastructure.Files;

/// <summary>
/// The owner's files on the local volume (<see cref="IFileBytes"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the only class in the product that opens a file the owner
/// stored</strong>, and the only one that turns a
/// <see cref="StorageAddress"/> into a path on somebody's operating system. The
/// separator swap happens here and nowhere else, so that the addresses in rows,
/// in logs and in tests are the same string on every platform.
/// </para>
/// <para>
/// <strong>The root is resolved once and every path is checked against it.</strong>
/// Nothing can reach this with a name — every address is derived from a
/// <see cref="Guid"/> — but the check is here anyway, because it costs one
/// comparison and it is the difference between "no caller can do this" and "no
/// caller can do this as long as every future caller keeps to the rule".
/// </para>
/// </remarks>
public sealed class LocalFileBytes : IFileBytes
{
    private readonly string root;
    private readonly ILogger<LocalFileBytes> logger;

    public LocalFileBytes(StorageRoot where, ILogger<LocalFileBytes> logger)
    {
        ArgumentNullException.ThrowIfNull(where);

        this.root = where.Path;
        this.logger = logger;
    }

    public async Task<BytesArrived> ReceiveAsync(
        Stream content, Guid id, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var arriving = PathOf(StorageAddress.Arriving(id));

        Directory.CreateDirectory(Path.GetDirectoryName(arriving)!);

        long written;

        try
        {
            written = await WriteAsync(content, arriving, maxBytes, cancellationToken);
        }
        catch
        {
            // Whatever went wrong — the limit, a broken connection, a full
            // disk — what is on the volume is half a file nobody asked for.
            // The tidy-up would get it within the hour; deleting it now is what
            // keeps an instance from filling up with the same failed upload
            // retried twenty times.
            Discard(arriving);
            throw;
        }

        var stored = PathOf(StorageAddress.Of(id));

        Directory.CreateDirectory(Path.GetDirectoryName(stored)!);

        // A move within one volume is atomic, which is what makes "the bytes
        // are there" a moment rather than an interval. An upload killed while
        // it was still arriving leaves a `.part`, never a short file at the
        // address a row is about to point at.
        File.Move(arriving, stored, overwrite: true);

        return new BytesArrived(id, StorageAddress.Of(id), written);
    }

    public Task<Stream> OpenAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = PathOf(StorageAddress.Of(id));

        try
        {
            return Task.FromResult<Stream>(new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan));
        }
        catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
        {
            // The row says there is a file and the volume says there is not.
            // That is not the caller's mistake and there is nothing they can do
            // about it, so they get `internal` and the operator gets the line
            // that names the file — this is the failure the storage volume
            // exists to prevent, and it should be loud.
            logger.LogError(
                missing,
                "The file {Id} has a row and no bytes at {Address}. Its storage volume may not be the one it "
                + "was stored on.",
                id,
                StorageAddress.Of(id));

            throw new Refusal(
                RefusalCode.Internal,
                "This file's bytes are not on this instance's storage volume.");
        }
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = PathOf(StorageAddress.Of(id));

        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);

        return Task.FromResult(true);
    }

    public Task<int> TidyAsync(
        IReadOnlySet<Guid> stored, DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var removed = 0;

        removed += Tidy(
            Path.Combine(root, StorageAddress.Incoming),
            olderThan,
            cancellationToken,
            // Everything in here is by definition unfinished: a `.part` older
            // than the margin is an upload whose connection went away.
            _ => true);

        removed += Tidy(
            Path.Combine(root, StorageAddress.Files),
            olderThan,
            cancellationToken,
            path =>
            {
                var address = AddressOf(path);

                // A name that is not an id where an id belongs is something
                // this product did not put there — an operator's own file, a
                // restore somebody unpacked by hand — and it is left alone.
                // A sweep that removes what it does not recognise is a sweep
                // that eventually removes something that mattered.
                return StorageAddress.IdAt(address) is { } id && !stored.Contains(id);
            });

        return Task.FromResult(removed);
    }

    /// <summary>
    /// Reads the stream into <paramref name="path"/>, stopping the moment it
    /// goes past the limit.
    /// </summary>
    /// <remarks>
    /// The limit is enforced against the bytes that actually arrive and never
    /// against <c>Content-Length</c>, which the caller writes and nothing
    /// checks. One buffer past the limit is what it costs to know, and stopping
    /// there is what keeps a stream that claims nothing from filling the volume.
    /// </remarks>
    private static async Task<long> WriteAsync(
        Stream content, string path, long maxBytes, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[64 * 1024];
        long written = 0;

        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                break;
            }

            written += read;

            if (written > maxBytes)
            {
                throw Refusal.TooLarge(
                    $"This file is larger than the {maxBytes / StorageSettings.Mebibyte} MiB one file may be "
                    + "on this instance.",
                    maxBytes);
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await file.FlushAsync(cancellationToken);

        return written;
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The upload has already failed and the caller is about to be told
            // why. Failing to clean up after it is the tidy-up's problem within
            // the hour, and not a second exception to replace the first.
        }
    }

    private int Tidy(
        string directory,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken,
        Func<string, bool> orphaned)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Written, not created: a file moved into place keeps the time
                // it was written, and a file still arriving has just been
                // touched. Either way the margin is about when somebody last
                // wrote to it.
                if (File.GetLastWriteTimeUtc(path) > olderThan.UtcDateTime || !orphaned(path))
                {
                    continue;
                }

                File.Delete(path);
                removed++;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // One file that will not go is not a reason to leave the rest.
                // The next sweep tries it again.
                logger.LogWarning(failure, "Could not tidy up {Path}.", path);
            }
        }

        return removed;
    }

    /// <summary>The address, as this product writes one, of a path on this disk.</summary>
    private string AddressOf(string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// The path an address is, on this disk — and the check that it is under the
    /// root after all.
    /// </summary>
    private string PathOf(string address)
    {
        var path = Path.GetFullPath(
            Path.Combine(root, address.Replace('/', Path.DirectorySeparatorChar)));

        var inside = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return path.StartsWith(inside, StringComparison.Ordinal)
            ? path
            : throw new InvalidOperationException(
                $"The address `{address}` is not under the storage root. Nothing should be able to produce "
                + "one: an address is made from an id and never from a name.");
    }
}
