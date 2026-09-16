using System.Formats.Tar;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Personalaffe.Api.Hosting;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// A backup, put back — and every way it refuses to put one back.
/// </summary>
/// <remarks>
/// <para>
/// The load is substituted, for the same reason the dump is in
/// <see cref="BackupTests"/>: the thing that restores a PostgreSQL is a
/// PostgreSQL, and a suite that needed one of the right version on the machine
/// running it would be a suite nobody runs. What is real here is everything
/// that decides whether the load happens at all — the manifest, the checksums,
/// the schema, what is already in the instance — and the file half, which is
/// this product's own and lands on a real disk.
/// </para>
/// <para>
/// That the whole circle works against the real tools is proven where they are
/// real: <c>scripts/rehearse-a-restore.sh</c>, against the image, in CI.
/// </para>
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class RestoreTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task It_puts_the_files_back_where_the_instance_looks_for_them()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres);

        var (code, said) = await taken.PutBackAsync();

        Assert.Equal(0, code);
        Assert.Contains("what that backup says it is", said, StringComparison.Ordinal);

        // The bytes, on the disk the instance reads from, under the name its
        // rows point at. Not a count of them.
        Assert.Equal(taken.Content, await File.ReadAllBytesAsync(taken.Stored, Token));

        // And what it loaded is the dump out of that archive and not some other
        // file that happened to be lying about.
        Assert.StartsWith("-- everything the owner has", taken.Loaded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_lets_go_of_the_pause_the_backup_was_holding()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres);

        // A dump is taken while the instance is held still, so the row saying so
        // is *in* the dump. Restoring it hands a brand new instance a pause that
        // a backup which finished minutes ago started, and the instance refuses
        // every write until that deadline passes — for a backup that is not
        // happening. The rehearsal found this by trying to write afterwards.
        await using (var scope = taken.Instance.Services.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IMaintenance>();
            var pause = await maintenance.ReadAsync(Token);

            pause.Begin(DateTimeOffset.UtcNow, MaintenancePause.Budget);

            await maintenance.SaveAsync(Token);
        }

        Assert.Equal(0, (await taken.PutBackAsync()).Code);

        await using (var scope = taken.Instance.Services.CreateAsyncScope())
        {
            var pause = await scope.ServiceProvider.GetRequiredService<IMaintenance>().ReadAsync(Token);

            Assert.False(pause.Holds(DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task An_archive_with_no_manifest_is_an_interrupted_backup_and_is_refused()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres);

        var (code, said) = await taken.PutBackAsync(taken.Without(Backup.Manifest));

        Assert.Equal(1, code);
        Assert.Contains("has no manifest.json", said, StringComparison.Ordinal);
        Assert.Contains("interrupted", said, StringComparison.Ordinal);
        Assert.Null(taken.Loaded);
        Assert.False(File.Exists(taken.Stored));
    }

    [Fact]
    public async Task A_dump_that_arrived_without_its_volume_is_caught_rather_than_half_restored()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres);

        // The manifest still names the file; the archive no longer carries it.
        // This is the shape of half a backup, and it is the one failure a
        // restore must not discover by serving a download that 404s.
        var (code, said) = await taken.PutBackAsync(taken.Without(taken.StoredIn));

        Assert.Equal(1, code);
        Assert.Contains("Half a backup is not a backup", said, StringComparison.Ordinal);
        Assert.Null(taken.Loaded);
    }

    [Fact]
    public async Task An_archive_whose_bytes_have_rotted_is_refused_by_its_checksum()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres);

        var (code, said) = await taken.PutBackAsync(
            taken.With(taken.StoredIn, Encoding.UTF8.GetBytes(new string('x', taken.Content.Length))));

        Assert.Equal(1, code);
        Assert.Contains("the right length and not the right bytes", said, StringComparison.Ordinal);
        Assert.Null(taken.Loaded);
    }

    [Fact]
    public async Task A_backup_from_a_newer_build_is_refused_the_way_a_newer_database_is()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres);

        var (code, said) = await taken.PutBackAsync(
            taken.Claiming(schema => schema.Add("20990101000000_TheSchemaOfSomeLaterEpic")));

        Assert.Equal(1, code);
        Assert.Contains("migrated by a newer personalaffe", said, StringComparison.Ordinal);
        Assert.Contains("20990101000000_TheSchemaOfSomeLaterEpic", said, StringComparison.Ordinal);
        Assert.Null(taken.Loaded);
    }

    [Fact]
    public async Task It_will_not_write_over_an_instance_that_has_something_in_it()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres, populated: true);

        var (code, said) = await taken.PutBackAsync();

        Assert.Equal(1, code);
        Assert.Contains("this instance is not empty", said, StringComparison.Ordinal);
        Assert.Contains(Restore.AnywayFlag, said, StringComparison.Ordinal);

        // The accidental path destroys nothing: what was here is still here and
        // the database was never touched.
        Assert.Null(taken.Loaded);
        Assert.True(File.Exists(taken.SomethingAlreadyHere));
    }

    [Fact]
    public async Task And_does_when_the_operator_says_so()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres, populated: true);

        var (code, _) = await taken.PutBackAsync(anyway: true);

        Assert.Equal(0, code);
        Assert.Equal(taken.Content, await File.ReadAllBytesAsync(taken.Stored, Token));

        // What was in the instance before is gone, which is what the flag means.
        Assert.False(File.Exists(taken.SomethingAlreadyHere));
    }

    [Fact]
    public async Task An_archive_that_points_outside_itself_is_not_one_of_ours()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres);

        var (code, said) = await taken.PutBackAsync(taken.Renaming(Backup.Dump, "../../escaped.sql"));

        Assert.Equal(1, code);
        Assert.Contains("points outside it", said, StringComparison.Ordinal);
        Assert.Null(taken.Loaded);
    }

    [Fact]
    public async Task Nothing_it_refused_leaves_anything_behind_under_the_storage_root()
    {
        await using var taken = await ARestore.OfABackupAsync(postgres);

        await taken.PutBackAsync(taken.Without(Backup.Manifest));

        // A refused restore unpacked an archive to check it. What it leaves is
        // nothing: a `restoring-…` directory nobody removed would be counted
        // against the storage quota forever, and would make the next restore
        // think the instance is populated.
        Assert.Empty(Directory.EnumerateDirectories(taken.Root));
    }

    /// <summary>An instance, a backup of another one, and the verb pointed at both.</summary>
    private sealed class ARestore : IAsyncDisposable
    {
        private readonly AnInstance _instance;

        /// <summary>The instance being restored into.</summary>
        public AnInstance Instance => _instance;
        private readonly byte[] _archive;

        /// <summary>The one the instance's services hand out, and not a second one.</summary>
        private readonly ALoad _load;

        private ARestore(
            AnInstance instance, string root, byte[] archive, string storedIn, byte[] content, ALoad load)
        {
            _instance = instance;
            _archive = archive;
            _load = load;
            Root = root;
            StoredIn = storedIn;
            Content = content;
        }

        /// <summary>The storage root of the instance being restored into.</summary>
        public string Root { get; }

        /// <summary>The one stored file's path inside the archive.</summary>
        public string StoredIn { get; }

        /// <summary>Its bytes, as they went in.</summary>
        public byte[] Content { get; }

        /// <summary>Where it should land.</summary>
        public string Stored =>
            Path.Combine(Root, StoredIn.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Something that was in this instance before the restore.</summary>
        public string SomethingAlreadyHere => Path.Combine(Root, "files", "already", "here");

        /// <summary>What the substituted tool was handed, or nothing if it never was.</summary>
        public string? Loaded => _load.Loaded;


        public static async Task<ARestore> OfABackupAsync(PostgresFixture postgres, bool populated = false)
        {
            var content = Encoding.UTF8.GetBytes("the owner's own file, and every byte of it");

            // One instance to take a backup of, and a second, empty one to put
            // it back into — which is what a restore onto a fresh instance is.
            var (archive, storedIn) = await ABackupOfSomethingAsync(postgres, content);

            var root = Path.Combine(Path.GetTempPath(), $"personalaffe-restore-{Guid.NewGuid():n}");
            Directory.CreateDirectory(root);

            var load = new ALoad();

            var instance = await AnInstance.StartedWithAsync(
                postgres,
                services => services.AddScoped<IDatabaseRestore>(_ => load),
                new Dictionary<string, string?> { [StorageSettings.Variable] = root });

            var taken = new ARestore(instance, root, archive, storedIn, content, load);

            if (populated)
            {
                await AnOwner.SetUpAsync(instance, TestContext.Current.CancellationToken);

                Directory.CreateDirectory(Path.Combine(root, "files", "already"));
                await File.WriteAllTextAsync(
                    Path.Combine(root, "files", "already", "here"),
                    "something this instance had",
                    TestContext.Current.CancellationToken);
            }

            return taken;
        }

        public Task<(int Code, string Said)> PutBackAsync(byte[]? archive = null, bool anyway = false)
        {
            var said = new StringWriter();

            return Run(archive ?? _archive, anyway, said);
        }

        public Task<(int Code, string Said)> PutBackAsync(bool anyway) =>
            PutBackAsync(archive: null, anyway);

        /// <summary>The same archive, without one of its entries.</summary>
        public byte[] Without(string entry) => Rewritten(entry, keep: false, replacement: null, renamed: null);

        /// <summary>The same archive, with one of its entries carrying something else.</summary>
        public byte[] With(string entry, byte[] replacement) =>
            Rewritten(entry, keep: true, replacement, renamed: null);

        /// <summary>The same archive, with one of its entries under another name.</summary>
        public byte[] Renaming(string entry, string renamed) =>
            Rewritten(entry, keep: true, replacement: null, renamed);

        /// <summary>The same archive, with a manifest that says something else.</summary>
        public byte[] Claiming(Action<JsonArray> schema)
        {
            var entries = Entries(_archive).ToList();
            var manifest = entries.FindIndex(entry => entry.Name == Backup.Manifest);
            var document = JsonNode.Parse(Encoding.UTF8.GetString(entries[manifest].Content))!;

            schema(document["schema"]!.AsArray());

            entries[manifest] = (Backup.Manifest, Encoding.UTF8.GetBytes(document.ToJsonString()));

            return Packed(entries);
        }

        public async ValueTask DisposeAsync()
        {
            await _instance.DisposeAsync();

            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A temporary directory that outlives the test is not a failure.
            }
        }

        private async Task<(int Code, string Said)> Run(byte[] archive, bool anyway, StringWriter said)
        {
            var code = await Restore.PutBackAsync(
                _instance.Services,
                StorageSettings.FromVariables(Root),
                () => new MemoryStream(archive),
                anyway,
                said,
                TimeProvider.System,
                Token);

            return (code, said.ToString());
        }

        private byte[] Rewritten(string entry, bool keep, byte[]? replacement, string? renamed)
        {
            var entries = new List<(string Name, byte[] Content)>();

            foreach (var (name, content) in Entries(_archive))
            {
                if (name != entry)
                {
                    entries.Add((name, content));
                }
                else if (keep)
                {
                    entries.Add((renamed ?? name, replacement ?? content));
                }
            }

            return Packed(entries);
        }

        private static IEnumerable<(string Name, byte[] Content)> Entries(byte[] archive)
        {
            using var reading = new TarReader(new MemoryStream(archive));

            while (reading.GetNextEntry() is { } entry)
            {
                using var content = new MemoryStream();
                entry.DataStream?.CopyTo(content);

                yield return (entry.Name, content.ToArray());
            }
        }

        private static byte[] Packed(IEnumerable<(string Name, byte[] Content)> entries)
        {
            var written = new MemoryStream();

            using (var archive = new TarWriter(written, TarEntryFormat.Pax, leaveOpen: true))
            {
                foreach (var (name, content) in entries)
                {
                    archive.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                    {
                        DataStream = new MemoryStream(content),
                    });
                }
            }

            return written.ToArray();
        }

        /// <summary>
        /// A real backup of a real instance, which is what the tests put back.
        /// </summary>
        private static async Task<(byte[] Archive, string StoredIn)> ABackupOfSomethingAsync(
            PostgresFixture postgres, byte[] content)
        {
            var root = Path.Combine(Path.GetTempPath(), $"personalaffe-backed-{Guid.NewGuid():n}");
            var dump = new ADumpOfNothingMuch();

            await using var instance = await AnInstance.StartedWithAsync(
                postgres,
                services => services.AddScoped<IDatabaseDump>(_ => dump),
                new Dictionary<string, string?> { [StorageSettings.Variable] = root });

            using var owner = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

            using (var body = new ByteArrayContent(content))
            {
                body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

                using var response = await owner.PostAsync(
                    "/api/files/content?name=notes.txt", body, TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }

            var written = new MemoryStream();

            var code = await Backup.TakeAsync(
                instance.Services,
                StorageSettings.FromVariables(root),
                "-",
                TextWriter.Null,
                () => new NeverClosing(written),
                TimeProvider.System,
                TestContext.Current.CancellationToken);

            Assert.Equal(0, code);

            var archive = written.ToArray();

            var storedIn = Entries(archive)
                .Select(entry => entry.Name)
                .Single(name => name.StartsWith("files/", StringComparison.Ordinal));

            Directory.Delete(root, recursive: true);

            return (archive, storedIn);
        }

        private sealed class ADumpOfNothingMuch : IDatabaseDump
        {
            public Task<string> ToolAsync(CancellationToken cancellationToken) =>
                Task.FromResult("a substituted pg_dump");

            public Task WriteAsync(Stream destination, CancellationToken cancellationToken) =>
                destination.WriteAsync(
                    "-- everything the owner has, as SQL\n"u8.ToArray(), cancellationToken).AsTask();
        }

        private sealed class ALoad : IDatabaseRestore
        {
            public string? Loaded { get; private set; }

            public Task<string> ToolAsync(CancellationToken cancellationToken) =>
                Task.FromResult("a substituted psql");

            public async Task LoadAsync(Stream dump, CancellationToken cancellationToken)
            {
                using var reading = new StreamReader(dump);

                Loaded = await reading.ReadToEndAsync(cancellationToken);
            }
        }

        private sealed class NeverClosing(MemoryStream written) : Stream
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
