using System.Globalization;

namespace Personalaffe.Domain.Tasks;

/// <summary>
/// Where a task sits in its list: one number, between its neighbours'.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A move has to change one row.</strong> The obvious scheme —
/// positions 1 to n, renumbered on every move — changes every row below the
/// one that moved, and in this workspace that means every other holder of one
/// of them is suddenly stale: the owner's phone, the browser on their desk, an
/// agent halfway through a write. The guard would be doing its job over a
/// change nobody made.
/// </para>
/// <para>
/// So a position is a number with room on either side of it, and a move is the
/// midpoint of the two it lands between. Nothing else is touched, and nobody
/// else's version moves.
/// </para>
/// <para>
/// <strong>The midpoint runs out, and that is handled rather than hoped
/// about.</strong> Halving a gap fifty-odd times exhausts what a double can
/// tell apart; <see cref="RoomBetween"/> is the question the act asks first,
/// and a list with no room left is renumbered. It takes about fifty moves into
/// the same gap to get there, which is rare and is the one case where a move is
/// a change to more than one row. Pretending it cannot happen is how two tasks
/// end up with one position and an order nobody can explain.
/// </para>
/// </remarks>
public static class Positions
{
    /// <summary>Where the first task in a list goes.</summary>
    public const double First = 1024;

    /// <summary>How far apart two tasks are put when a list is renumbered.</summary>
    public const double Apart = 1024;

    /// <summary>
    /// Whether there is a number strictly between these two.
    /// </summary>
    /// <remarks>
    /// Asked before the midpoint is taken, because a midpoint that equals one
    /// of its neighbours is an order that has quietly collapsed.
    /// </remarks>
    public static bool RoomBetween(double above, double below)
    {
        var middle = above + ((below - above) / 2);

        return middle > above && middle < below;
    }

    /// <summary>
    /// The position of something placed between <paramref name="above"/> and
    /// <paramref name="below"/>. Nothing above means the top of the list;
    /// nothing below means the end of it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// There is no room between the two. The caller asks
    /// <see cref="RoomBetween"/> first and renumbers instead.
    /// </exception>
    public static double Between(double? above, double? below) => (above, below) switch
    {
        (null, null) => First,

        // At the top: half of what is currently first, so the list keeps going
        // down towards zero and never has to shuffle to make room at the front.
        (null, { } first) => first / 2,

        // At the end: a whole step past what is currently last.
        ({ } last, null) => last + Apart,

        var (one, other) when RoomBetween(one.Value, other.Value) =>
            one.Value + ((other.Value - one.Value) / 2),

        _ => throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"There is no position between {above} and {below}. Renumber the list first.")),
    };

    /// <summary>
    /// The positions a list is renumbered to: evenly spaced, in the order given.
    /// </summary>
    public static IReadOnlyList<double> Renumbered(int count) =>
        [.. Enumerable.Range(1, count).Select(place => place * Apart)];
}
