using Personalaffe.Domain.Tasks;

namespace Personalaffe.UnitTests;

/// <summary>
/// Where a task sits: the arithmetic that makes a move one row rather than a
/// renumbering of everything below it.
/// </summary>
public sealed class PositionsTests
{
    [Fact]
    public void The_first_task_in_a_list_gets_a_number_with_room_on_both_sides()
    {
        var first = Positions.Between(null, null);

        Assert.Equal(Positions.First, first);

        // Room above it as well as below, so that putting something at the top
        // never has to shuffle what is already there.
        Assert.True(Positions.Between(null, first) < first);
        Assert.True(Positions.Between(first, null) > first);
    }

    [Fact]
    public void Between_two_neighbours_is_between_them()
    {
        var middle = Positions.Between(10, 20);

        Assert.True(middle > 10);
        Assert.True(middle < 20);
    }

    [Fact]
    public void A_hundred_moves_into_the_same_place_stay_in_order()
    {
        // Each one goes between the top and what used to be first, which is the
        // worst case: the gap halves every time.
        var order = new List<double> { Positions.Between(null, null) };

        for (var move = 0; move < 100 && Positions.RoomBetween(0, order[0]); move++)
        {
            order.Insert(0, Positions.Between(null, order[0]));
        }

        Assert.Equal(order.OrderBy(position => position), order);
        Assert.Equal(order.Distinct().Count(), order.Count);
    }

    [Fact]
    public void A_gap_with_nothing_in_it_says_so_rather_than_colliding()
    {
        // Two doubles with nothing between them. Pretending otherwise is how
        // two tasks end up with one position and an order nobody can explain.
        const double one = 1;
        var next = Math.BitIncrement(one);

        Assert.False(Positions.RoomBetween(one, next));
        Assert.Throws<InvalidOperationException>(() => Positions.Between(one, next));

        Assert.True(Positions.RoomBetween(one, 2));
    }

    [Fact]
    public void A_renumbered_list_is_evenly_spaced_and_in_the_order_it_was_given()
    {
        var renumbered = Positions.Renumbered(4);

        Assert.Equal(4, renumbered.Count);
        Assert.Equal(renumbered.OrderBy(position => position), renumbered);

        // Evenly, so that the next move between any two of them has as much room
        // as the one before it.
        var gaps = renumbered.Zip(renumbered.Skip(1), (one, next) => next - one).Distinct();
        Assert.Single(gaps);
    }

    [Fact]
    public void Renumbering_nothing_is_nothing()
    {
        Assert.Empty(Positions.Renumbered(0));
    }
}
