using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// The short stillness a backup needs, and the two things that make it safe to
/// have at all: it holds against every process over this database, and it ends
/// whether or not anything comes back to end it.
/// </summary>
public sealed class MaintenancePauseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_instance_is_not_being_held_still()
    {
        var pause = MaintenancePause.Nothing(Now);

        Assert.False(pause.Holds(Now));
        Assert.Null(pause.Since);
        Assert.Null(pause.Until);
        Assert.Equal(TimeSpan.Zero, pause.Remaining(Now));
    }

    [Fact]
    public void Beginning_holds_it_for_the_budget_and_no_longer()
    {
        var pause = MaintenancePause.Nothing(Now);

        pause.Begin(Now, TimeSpan.FromMinutes(5));

        Assert.True(pause.Holds(Now));
        Assert.True(pause.Holds(Now.AddMinutes(4)));
        Assert.Equal(TimeSpan.FromMinutes(1), pause.Remaining(Now.AddMinutes(4)));

        // The moment it runs out, and every moment after it. This is the whole
        // of what makes a killed backup survivable: nothing has to come back.
        Assert.False(pause.Holds(Now.AddMinutes(5)));
        Assert.False(pause.Holds(Now.AddDays(1)));
    }

    [Fact]
    public void Two_backups_cannot_hold_it_at_once()
    {
        var pause = MaintenancePause.Nothing(Now);
        pause.Begin(Now, MaintenancePause.Budget);

        var refused = Assert.Throws<Refusal>(() => pause.Begin(Now.AddSeconds(1), MaintenancePause.Budget));

        Assert.Equal(RefusalCode.Conflict, refused.Code);
        Assert.Contains("already being held still", refused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_backup_that_never_came_back_does_not_block_the_next_one()
    {
        var pause = MaintenancePause.Nothing(Now);
        pause.Begin(Now, MaintenancePause.Budget);

        // Nothing ended it; the deadline did. An instance whose backup was
        // killed is writable again, and backable-up again, without anybody
        // finding a row to edit by hand.
        pause.Begin(Now + MaintenancePause.Budget, MaintenancePause.Budget);

        Assert.True(pause.Holds(Now + MaintenancePause.Budget));
    }

    [Fact]
    public void Extending_pushes_the_deadline_out_and_keeps_the_moment_it_began()
    {
        var pause = MaintenancePause.Nothing(Now);
        pause.Begin(Now, MaintenancePause.Budget);

        pause.Extend(Now.AddMinutes(2), MaintenancePause.Budget);

        Assert.Equal(Now, pause.Since);
        Assert.Equal(Now.AddMinutes(2) + MaintenancePause.Budget, pause.Until);
    }

    [Fact]
    public void Ending_lets_go_whether_or_not_it_was_holding()
    {
        var pause = MaintenancePause.Nothing(Now);
        pause.Begin(Now, MaintenancePause.Budget);

        pause.End(Now.AddMinutes(1));

        Assert.False(pause.Holds(Now.AddMinutes(1)));
        Assert.Null(pause.Since);
        Assert.Null(pause.Until);

        pause.End(Now.AddMinutes(2));
        Assert.False(pause.Holds(Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60 * 60 + 1)]
    public void A_budget_outside_its_bounds_is_a_mistake_and_not_a_pause(int seconds)
    {
        var pause = MaintenancePause.Nothing(Now);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => pause.Begin(Now, TimeSpan.FromSeconds(seconds)));

        // And nothing was changed by the attempt.
        Assert.False(pause.Holds(Now));
    }

    [Fact]
    public void Every_write_moves_the_version_two_backups_are_told_apart_by()
    {
        var pause = MaintenancePause.Nothing(Now);

        pause.Begin(Now.AddMinutes(1), MaintenancePause.Budget);
        Assert.Equal(Now.AddMinutes(1), pause.UpdatedAt);

        pause.Extend(Now.AddMinutes(2), MaintenancePause.Budget);
        Assert.Equal(Now.AddMinutes(2), pause.UpdatedAt);

        pause.End(Now.AddMinutes(3));
        Assert.Equal(Now.AddMinutes(3), pause.UpdatedAt);
    }
}
