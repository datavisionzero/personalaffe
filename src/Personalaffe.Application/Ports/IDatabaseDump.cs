namespace Personalaffe.Application.Ports;

/// <summary>
/// Half of a backup: everything in the database, in a form something else can
/// put back.
/// </summary>
/// <remarks>
/// <para>
/// A port, and not a <c>Process.Start</c> in the middle of the backup, for one
/// reason: <strong>the thing that dumps a PostgreSQL is a PostgreSQL</strong>.
/// This product does not know how to write a dump and must never learn — what
/// it knows is when to ask for one, what to do with it, and what to say when
/// the tool that takes it is not there or is older than the server it is
/// pointed at.
/// </para>
/// <para>
/// It is also what lets everything around the dump be tested without a
/// PostgreSQL client on the machine running the tests: the pause, the stillness
/// of the sweeps, the archive, the manifest and the checksums are the product's
/// own and are proven against a substituted dump. That the real one works is
/// proven where it is real — against the image, in the rehearsal CI runs.
/// </para>
/// </remarks>
public interface IDatabaseDump
{
    /// <summary>
    /// What will take the dump, as it names itself — for the manifest, and so
    /// that a restore can say what it is looking at.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tool is missing, or it is older than the server it is pointed at and
    /// would refuse. Either is a sentence for the operator and not a stack
    /// trace: a backup that half-happened is worse than one that did not start.
    /// </exception>
    Task<string> ToolAsync(CancellationToken cancellationToken);

    /// <summary>Writes the dump to <paramref name="destination"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The dump did not finish. What it said is in the message.
    /// </exception>
    Task WriteAsync(Stream destination, CancellationToken cancellationToken);
}

/// <summary>
/// The other direction: a dump, put back into the database it came out of a
/// shape of.
/// </summary>
/// <remarks>
/// A port for the same reason <see cref="IDatabaseDump"/> is one — the thing
/// that restores a PostgreSQL is a PostgreSQL — and separate from it because
/// the two are used by different verbs at different moments, and an instance
/// that can only ever take a backup should not carry the ability to overwrite
/// itself in the same interface.
/// </remarks>
public interface IDatabaseRestore
{
    /// <summary>
    /// What will put the dump back, as it names itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool is missing.</exception>
    Task<string> ToolAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads <paramref name="dump"/> into the database, replacing what is there.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// It did not finish, and the database is now neither what it was nor what
    /// the dump says. What the tool said is in the message.
    /// </exception>
    Task LoadAsync(Stream dump, CancellationToken cancellationToken);
}
