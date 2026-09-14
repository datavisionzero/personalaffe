using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// Putting something back into a tree: what comes back with it, what is in the
/// way, and what happens when the place it came from is gone for good.
/// </summary>
public sealed class RestorationTests
{
    private static readonly Placement Notes = new(Guid.CreateVersion7(), "notes", Deleted: false);
    private static readonly Placement Work = new(Guid.CreateVersion7(), "work", Deleted: false);

    [Fact]
    public void A_thing_whose_folders_are_all_there_comes_back_on_its_own()
    {
        var page = new Placement(Guid.CreateVersion7(), "architecture", Deleted: true);

        var plan = Restoration.Plan([page, Notes, Work], reachesTheRoot: true, restoreAs: null, []);

        Assert.Equal([page.Id], plan.Restore);
        Assert.Equal("architecture", plan.Name);
        Assert.Equal(Notes.Id, plan.Parent);
        Assert.False(plan.MovedToTheRoot);
    }

    [Fact]
    public void A_thing_whose_folders_are_in_the_trash_brings_them_back_with_it()
    {
        var page = new Placement(Guid.CreateVersion7(), "architecture", Deleted: true);
        var notes = Notes with { Deleted = true };

        var plan = Restoration.Plan([page, notes, Work], reachesTheRoot: true, restoreAs: null, []);

        // The folder comes back because the alternative is a page the owner
        // cannot reach, or an owner walking the tree by hand.
        Assert.Equal([page.Id, notes.Id], plan.Restore);
        Assert.Equal(notes.Id, plan.Parent);
    }

    [Fact]
    public void Only_the_folders_that_are_actually_gone_come_back()
    {
        var page = new Placement(Guid.CreateVersion7(), "architecture", Deleted: true);
        var work = Work with { Deleted = true };

        var plan = Restoration.Plan([page, Notes, work], reachesTheRoot: true, restoreAs: null, []);

        Assert.Equal([page.Id, work.Id], plan.Restore);
        Assert.DoesNotContain(Notes.Id, plan.Restore);
    }

    [Fact]
    public void A_thing_at_the_root_has_no_parent()
    {
        var page = new Placement(Guid.CreateVersion7(), "architecture", Deleted: true);

        var plan = Restoration.Plan([page], reachesTheRoot: true, restoreAs: null, []);

        Assert.Null(plan.Parent);
        Assert.False(plan.MovedToTheRoot);
    }

    [Fact]
    public void A_name_already_in_the_place_is_refused_and_the_occupant_is_named()
    {
        var page = new Placement(Guid.CreateVersion7(), "architecture", Deleted: true);

        var refusal = Assert.Throws<Refusal>(() => Restoration.Plan(
            [page, Notes], reachesTheRoot: true, restoreAs: null, ["architecture"]));

        Assert.Equal(RefusalCode.Conflict, refusal.Code);
        Assert.Contains("architecture", refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_in_the_way_is_in_the_way_whatever_its_case()
    {
        var page = new Placement(Guid.CreateVersion7(), "Architecture", Deleted: true);

        Assert.Throws<Refusal>(() => Restoration.Plan(
            [page, Notes], reachesTheRoot: true, restoreAs: null, ["architecture"]));
    }

    [Fact]
    public void The_same_call_takes_another_name_so_nobody_is_ever_stuck()
    {
        var page = new Placement(Guid.CreateVersion7(), "architecture", Deleted: true);

        var plan = Restoration.Plan(
            [page, Notes], reachesTheRoot: true, restoreAs: " architecture (old) ", ["architecture"]);

        Assert.Equal("architecture (old)", plan.Name);
        Assert.Equal(Notes.Id, plan.Parent);
    }

    [Fact]
    public void A_name_that_is_not_a_name_is_refused_as_one()
    {
        var page = new Placement(Guid.CreateVersion7(), "architecture", Deleted: true);

        var refusal = Assert.Throws<Refusal>(() => Restoration.Plan(
            [page, Notes], reachesTheRoot: true, restoreAs: "   ", []));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
    }

    [Fact]
    public void When_the_place_it_came_from_is_gone_for_good_it_goes_to_the_root_and_says_so()
    {
        var page = new Placement(Guid.CreateVersion7(), "architecture", Deleted: true);

        var plan = Restoration.Plan([page], reachesTheRoot: false, restoreAs: null, []);

        Assert.True(plan.MovedToTheRoot);
        Assert.Null(plan.Parent);
        Assert.Equal([page.Id], plan.Restore);
    }

    [Fact]
    public void A_chain_has_to_start_with_the_thing_being_restored()
    {
        Assert.Throws<ArgumentException>(
            () => Restoration.Plan([], reachesTheRoot: true, restoreAs: null, []));
    }
}
