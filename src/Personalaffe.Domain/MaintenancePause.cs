namespace Personalaffe.Domain;

/// <summary>
/// The short stillness a backup needs, so that the database and the file volume
/// can be taken as of one moment (<c>docs/operations.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A file is a row in one store and bytes in the other, and neither
/// half is the file.</strong> A backup that took them at two different moments
/// would be a backup with rows whose files are missing — and a row without its
/// bytes is the one failure a restore cannot repair, because the bytes are
/// simply not anywhere. VISION §9 permits a short maintenance pause for exactly
/// this, and this is it.
/// </para>
/// <para>
/// <strong>Reads stay up.</strong> What has to hold still is what the two
/// stores could disagree about, and that is writing. An owner who opens the
/// workspace during a backup sees it, can read every page and download every
/// file, and is told — in a sentence rather than a status — that a write has to
/// wait a moment.
/// </para>
/// <para>
/// <strong>It ends on its own.</strong> The pause is a deadline and not a flag:
/// a backup that is killed between two of its steps leaves a row saying "still
/// until 10:14", and at 10:14 the instance is writable again whether or not
/// anything came back to say so. A flag would leave an instance that refuses
/// every write until somebody who has gone home logs in — which is a worse
/// outage than the one the backup was protecting against.
/// </para>
/// <para>
/// It says nothing about <em>what</em> is holding the instance still, because
/// only one thing ever does. The day something else wants a pause is the day
/// this gains a reason, and it will be a column rather than a guess.
/// </para>
/// </remarks>
public sealed class MaintenancePause
{
    /// <summary>
    /// The longest a pause may be asked for.
    /// </summary>
    /// <remarks>
    /// An hour is far longer than a backup of a personal workspace takes and
    /// short enough that the worst a forgotten pause can do is an hour of
    /// refused writes. There is no unbounded value: a pause nobody can outlast
    /// is an outage with a nicer name.
    /// </remarks>
    public static readonly TimeSpan Longest = TimeSpan.FromHours(1);

    /// <summary>The shortest, so that a mistyped budget cannot be no pause at all.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(30);

    /// <summary>
    /// What a backup asks for, and what it keeps pushing out while it works.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The key and the check constraint both, which is how a table keeps itself
    /// to one row (<c>WeatherPlace</c>).
    /// </summary>
    public bool Singleton { get; private set; } = true;

    /// <summary>When the instance was last held still, or nothing.</summary>
    public DateTimeOffset? Since { get; private set; }

    /// <summary>
    /// When it stops being held still, or nothing. A moment in the past is not
    /// a pause: see <see cref="Holds"/>.
    /// </summary>
    public DateTimeOffset? Until { get; private set; }

    /// <summary>
    /// The version, and the reason two backups cannot begin at the same
    /// instant: <see cref="Begin"/> reads and then writes, and a concurrency
    /// token is what turns that pair into one decision.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// The row as the migration creates it: an instance that is not being held
    /// still, with a version to be written against from the first moment.
    /// </summary>
    public static MaintenancePause Nothing(DateTimeOffset now) =>
        new() { Singleton = true, UpdatedAt = now };

    /// <summary>Whether writes are being refused at <paramref name="now"/>.</summary>
    public bool Holds(DateTimeOffset now) => Until is { } until && now < until;

    /// <summary>How much longer, for the caller who is told to come back.</summary>
    public TimeSpan Remaining(DateTimeOffset now) =>
        Holds(now) ? Until!.Value - now : TimeSpan.Zero;

    /// <summary>
    /// Holds the instance still until <paramref name="now"/> plus
    /// <paramref name="budget"/>.
    /// </summary>
    /// <exception cref="Refusal">
    /// <c>conflict</c> when something is already holding it — two backups over
    /// one instance would take two halves of two different moments.
    /// </exception>
    public void Begin(DateTimeOffset now, TimeSpan budget)
    {
        if (Holds(now))
        {
            throw Refusal.Conflict(
                "This instance is already being held still, until "
                + $"{Until!.Value.UtcDateTime:HH:mm:ss}Z. Something else is backing it up, or "
                + "something was and has not come back to say it finished — in which case the "
                + "pause lapses by itself at that moment.");
        }

        Since = now;
        Until = now + Bounded(budget);
        UpdatedAt = now;
    }

    /// <summary>
    /// Pushes the deadline out again, which is what a backup does while it
    /// works. Doing this to a pause that has already lapsed begins a new one:
    /// the alternative is a backup that carries on with the instance writable
    /// behind it.
    /// </summary>
    public void Extend(DateTimeOffset now, TimeSpan budget)
    {
        Since ??= now;
        Until = now + Bounded(budget);
        UpdatedAt = now;
    }

    /// <summary>Lets go, whether or not it was holding.</summary>
    public void End(DateTimeOffset now)
    {
        Since = null;
        Until = null;
        UpdatedAt = now;
    }

    private static TimeSpan Bounded(TimeSpan budget) =>
        budget < Shortest || budget > Longest
            ? throw new ArgumentOutOfRangeException(
                nameof(budget),
                budget,
                $"A maintenance pause is between {Shortest.TotalSeconds:0} seconds and "
                + $"{Longest.TotalMinutes:0} minutes.")
            : budget;
}
