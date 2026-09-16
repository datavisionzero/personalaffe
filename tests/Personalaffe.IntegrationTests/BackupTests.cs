using System.Formats.Tar;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Hosting;
using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// One backup, both stores, and the pause that makes them agree.
/// </summary>
/// <remarks>
/// <para>
/// The dump is substituted and everything else is real: the pause in the
/// database, the writes meeting it, the lock the sweep tries for, the archive,
/// the manifest and its checksums. What is substituted is the one part this
/// product does not implement — the thing that dumps a PostgreSQL is a
/// PostgreSQL — and what that buys is a suite that runs on a machine with no
/// PostgreSQL client on it. That the real <c>pg_dump</c> works is proven where
/// it is real: against the image, in the rehearsal.
/// </para>
/// <para>
/// A dump that can be told to stop in the middle is what makes the pause
/// observable at all. Everything below that asks "and what does the instance do
/// meanwhile" holds it there and asks.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class BackupTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task It_carries_both_stores_and_a_manifest_that_describes_them()
    {
        await using var backing = await ABackup.OfAnInstanceAsync(postgres);

        var bytes = Encoding.UTF8.GetBytes("the owner's own file, and every byte of it");
        await backing.UploadAsync("notes.txt", bytes);

        var (code, archive) = await backing.TakeAsync();

        Assert.Equal(0, code);

        var entries = Read(archive);

        // The database first, the files after it, and the manifest last.
        Assert.Equal(Backup.Dump, entries[0].Name);
        Assert.Equal(Backup.Manifest, entries[^1].Name);
        Assert.Contains(entries, entry => entry.Name.StartsWith("files/", StringComparison.Ordinal));

        var manifest = JsonNode.Parse(Encoding.UTF8.GetString(entries[^1].Content))!;

        Assert.Equal(ADump.Tool, manifest["dumped_by"]!.GetValue<string>());
        Assert.NotEmpty(manifest["version"]!.GetValue<string>());
        Assert.NotEmpty(manifest["schema"]!.AsArray());
        Assert.Equal(Checksum(entries[0].Content), manifest["database"]!["sha256"]!.GetValue<string>());

        // Every stored file, by what it weighs and what it hashes to — which is
        // what a restore checks rather than counting rows.
        var stored = manifest["files"]!.AsArray();
        Assert.Single(stored);

        var file = entries.Single(entry => entry.Name == stored[0]!["path"]!.GetValue<string>());

        Assert.Equal(bytes, file.Content);
        Assert.Equal(bytes.Length, stored[0]!["bytes"]!.GetValue<long>());
        Assert.Equal(Checksum(bytes), stored[0]!["sha256"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_instance_that_has_never_stored_a_file_is_backed_up_all_the_same()
    {
        await using var backing = await ABackup.OfAnInstanceAsync(postgres);

        // `files/` under the storage root is made by the first upload, so a
        // fresh instance has no such directory. That is an instance with no
        // files and not a reason to refuse it a backup — which is what this
        // did, until this test.
        var (code, archive) = await backing.TakeAsync();

        Assert.Equal(0, code);

        var entries = Read(archive);

        Assert.Equal([Backup.Dump, Backup.Manifest], entries.Select(entry => entry.Name));
        Assert.Empty(JsonNode.Parse(Encoding.UTF8.GetString(entries[^1].Content))!["files"]!.AsArray());
    }

    [Fact]
    public async Task A_write_waits_while_it_holds_and_a_read_does_not()
    {
        await using var backing = await ABackup.OfAnInstanceAsync(postgres);
        await backing.UploadAsync("notes.txt", "something to carry"u8.ToArray());

        var taking = await backing.HeldInTheMiddleAsync();

        using (var refused = await backing.Owner.PostAsJsonAsync(
                   "/api/scratchpad/entries", new { text = "written mid-backup" }, Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);

            var document = JsonNode.Parse(await refused.Content.ReadAsStringAsync(Token))!;

            Assert.Equal("/problems/paused", document["type"]!.GetValue<string>());

            // A sentence a person can act on, and a number a client can.
            Assert.Contains("being backed up", document["detail"]!.GetValue<string>(), StringComparison.Ordinal);

            // How long before it is worth asking again, and not how long the
            // pause may last: the deadline behind it is five minutes, and a
            // client told to wait five minutes for half a second of stillness
            // has been given a worse answer than none.
            Assert.InRange(
                refused.Headers.RetryAfter!.Delta!.Value,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5));
        }

        // And the workspace is open the whole time: this is a pause and not an
        // outage, and everything the owner has written is still there to read.
        using (var read = await backing.Owner.GetAsync("/api/files", Token))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        Assert.Equal(0, await backing.ReleaseAsync(taking));

        // And the moment it lets go, the same write is taken.
        using var accepted = await backing.Owner.PostAsJsonAsync(
            "/api/scratchpad/entries", new { text = "written after it" }, Token);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task No_sweep_runs_while_it_holds()
    {
        await using var backing = await ABackup.OfAnInstanceAsync(postgres);

        var taking = await backing.HeldInTheMiddleAsync();

        // Not timed and not hoped for: the backup holds the lock the purge
        // takes, so the purge that would have destroyed content between the
        // dump and the archive does not run at all, and says so.
        await using (var scope = backing.Instance.Services.CreateAsyncScope())
        {
            var purge = await scope.ServiceProvider
                .GetRequiredService<PurgeTheTrash>()
                .ExecuteAsync(Token);

            Assert.False(purge.Swept);
            Assert.Equal(0, purge.Total);
        }

        Assert.Equal(0, await backing.ReleaseAsync(taking));

        // And afterwards it sweeps again, because a backup is a pause and not a
        // switch somebody has to turn back on.
        await using (var scope = backing.Instance.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider
                .GetRequiredService<PurgeTheTrash>()
                .ExecuteAsync(Token)).Swept);
        }
    }

    [Fact]
    public async Task A_second_backup_is_refused_rather_than_taking_half_of_another_moment()
    {
        await using var backing = await ABackup.OfAnInstanceAsync(postgres);

        var taking = await backing.HeldInTheMiddleAsync();

        var (code, said) = await backing.TakeAnotherAsync();

        Assert.Equal(1, code);
        Assert.Contains("already being held still", said, StringComparison.Ordinal);

        Assert.Equal(0, await backing.ReleaseAsync(taking));
    }

    [Fact]
    public async Task A_backup_that_dies_halfway_leaves_a_serving_instance_and_nothing_that_looks_like_one()
    {
        await using var backing = await ABackup.OfAnInstanceAsync(postgres);

        backing.Dump.Throws = new InvalidOperationException("pg_dump stopped with 1");

        var (code, archive) = await backing.TakeAsync();

        Assert.Equal(1, code);

        // Whatever was written — here, nothing at all, because the dump never
        // produced anything to archive — cannot be mistaken for a backup. The
        // manifest is written last on purpose, and an archive without one is
        // what a restore refuses: the property holds for a tar that stopped
        // anywhere, not only for one that never started.
        Assert.DoesNotContain(Read(archive), entry => entry.Name == Backup.Manifest);

        // And the instance is writable again straight away rather than in five
        // minutes' time when the deadline catches up.
        using var accepted = await backing.Owner.PostAsJsonAsync(
            "/api/scratchpad/entries", new { text = "after a backup that failed" }, Token);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task A_pause_nothing_ever_ended_lapses_by_itself()
    {
        var clock = new Moving(new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero));
        await using var backing = await ABackup.OfAnInstanceAsync(postgres, clock);

        // What a backup killed between two of its steps leaves behind: a row
        // saying "still until 10:05", and nothing that is ever coming back.
        await using (var scope = backing.Instance.Services.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IMaintenance>();
            var pause = await maintenance.ReadAsync(Token);

            pause.Begin(clock.GetUtcNow(), MaintenancePause.Budget);

            await maintenance.SaveAsync(Token);
        }

        using (var refused = await backing.Owner.PostAsJsonAsync(
                   "/api/scratchpad/entries", new { text = "during" }, Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        }

        clock.Now += MaintenancePause.Budget;

        using var accepted = await backing.Owner.PostAsJsonAsync(
            "/api/scratchpad/entries", new { text = "after the deadline" }, Token);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    private static string Checksum(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static IReadOnlyList<(string Name, byte[] Content)> Read(byte[] archive)
    {
        var entries = new List<(string, byte[])>();

        using var reading = new TarReader(new MemoryStream(archive));

        while (reading.GetNextEntry() is { } entry)
        {
            using var content = new MemoryStream();
            entry.DataStream?.CopyTo(content);

            entries.Add((entry.Name, content.ToArray()));
        }

        return entries;
    }

    private sealed class Moving(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// The thing that dumps a PostgreSQL, stood in for — and able to stop in the
    /// middle, which is what makes the pause something a test can look at.
    /// </summary>
    private sealed class ADump : IDatabaseDump
    {
        public const string Tool = "a substituted pg_dump, against PostgreSQL 18";

        private static readonly byte[] Content = "-- everything the owner has, as SQL\n"u8.ToArray();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Throws { get; set; }

        public bool Waits { get; set; }

        public Task<string> ToolAsync(CancellationToken cancellationToken) => Task.FromResult(Tool);

        public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
        {
            Started.TrySetResult();

            if (Waits)
            {
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            if (Throws is not null)
            {
                throw Throws;
            }

            await destination.WriteAsync(Content, cancellationToken);
        }
    }

    /// <summary>An instance, its owner, and the backup verb pointed at both.</summary>
    private sealed class ABackup : IAsyncDisposable
    {
        private readonly string _to;

        private ABackup(
            AnInstance instance,
            HttpClient owner,
            StorageSettings storage,
            string to,
            TimeProvider clock,
            ADump dump)
        {
            Instance = instance;
            Owner = owner;
            Storage = storage;
            _to = to;
            Clock = clock;
            Dump = dump;
        }

        public AnInstance Instance { get; }

        public HttpClient Owner { get; }

        public StorageSettings Storage { get; }

        public TimeProvider Clock { get; }

        /// <summary>The one the instance's services hand out, and not a second one.</summary>
        public ADump Dump { get; }

        public static async Task<ABackup> OfAnInstanceAsync(PostgresFixture postgres, TimeProvider? clock = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"personalaffe-backup-{Guid.NewGuid():n}");
            var to = Path.Combine(root, "taken");

            Directory.CreateDirectory(to);

            var dump = new ADump();

            var instance = await AnInstance.StartedWithAsync(
                postgres,
                services =>
                {
                    services.AddScoped<IDatabaseDump>(_ => dump);

                    if (clock is not null)
                    {
                        services.AddSingleton(clock);
                    }
                },
                new Dictionary<string, string?> { [StorageSettings.Variable] = root });

            var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

            return new ABackup(
                instance,
                owner,
                StorageSettings.FromVariables(root),
                to,
                clock ?? TimeProvider.System,
                dump);
        }

        public async Task UploadAsync(string name, byte[] content)
        {
            using var body = new ByteArrayContent(content);
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

            using var response = await Owner.PostAsync(
                $"/api/files/content?name={Uri.EscapeDataString(name)}", body, Token);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        /// <summary>Takes it, and hands back what it wrote.</summary>
        public async Task<(int Code, byte[] Archive)> TakeAsync()
        {
            var written = new MemoryStream();

            var code = await Backup.TakeAsync(
                Instance.Services,
                Storage,
                "-",
                TextWriter.Null,
                () => new Leaving(written),
                Clock,
                Token);

            return (code, written.ToArray());
        }

        /// <summary>Starts one and comes back while it is holding the instance still.</summary>
        public async Task<Task<int>> HeldInTheMiddleAsync()
        {
            Dump.Waits = true;

            var taking = Task.Run(async () => (await TakeAsync()).Code, Token);

            await Dump.Started.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

            return taking;
        }

        public async Task<int> ReleaseAsync(Task<int> taking)
        {
            Dump.Release.TrySetResult();

            return await taking.WaitAsync(TimeSpan.FromSeconds(30), Token);
        }

        /// <summary>A second backup, and what it said about the first.</summary>
        public async Task<(int Code, string Said)> TakeAnotherAsync()
        {
            var said = new StringWriter();

            var code = await Backup.TakeAsync(
                Instance.Services,
                Storage,
                _to,
                said,
                () => new MemoryStream(),
                Clock,
                Token);

            return (code, said.ToString());
        }

        public async ValueTask DisposeAsync()
        {
            Owner.Dispose();
            await Instance.DisposeAsync();

            try
            {
                Directory.Delete(Storage.Root, recursive: true);
            }
            catch (IOException)
            {
                // A temporary directory that outlives the test is not a failure.
            }
        }

        /// <summary>
        /// Standard output, as a test holds it: closed by the writer and still
        /// readable afterwards.
        /// </summary>
        private sealed class Leaving(MemoryStream written) : Stream
        {
            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => written.Length;

            public override long Position
            {
                get => written.Position;
                set => throw new NotSupportedException();
            }

            public override void Flush() => written.Flush();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                written.Write(buffer, offset, count);

            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
                written.WriteAsync(buffer, cancellationToken);
        }
    }
}
