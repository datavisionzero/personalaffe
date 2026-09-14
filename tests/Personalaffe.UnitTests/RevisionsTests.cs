using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>How much history is kept, and which of it goes.</summary>
public sealed class RevisionsTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record ARevision(DateTimeOffset At, Actor By) : IRevision;

    [Fact]
    public void Nothing_is_superseded_while_there_is_room()
    {
        Assert.Empty(Revisions.Superseded(Made(Revisions.Kept)));
    }

    [Fact]
    public void The_oldest_beyond_what_is_kept_are_the_ones_that_go()
    {
        var all = Made(Revisions.Kept + 3);

        var superseded = Revisions.Superseded(all);

        Assert.Equal(3, superseded.Count);
        Assert.Equal([Start, Start.AddHours(1), Start.AddHours(2)], superseded.Select(r => r.At).Order());
    }

    [Fact]
    public void The_order_they_arrive_in_does_not_decide_which_go()
    {
        var shuffled = Made(Revisions.Kept + 2).Reverse().ToArray();

        Assert.Equal(
            [Start, Start.AddHours(1)],
            Revisions.Superseded(shuffled).Select(revision => revision.At).Order());
    }

    [Fact]
    public void History_that_is_kept_by_count_survives_being_old()
    {
        // The reason it is a count and not an age: a page edited twice a year
        // deserves its history as much as one edited twice a day.
        var yearsApart = Enumerable
            .Range(0, 5)
            .Select(each => new ARevision(Start.AddYears(-each), TheOwner))
            .ToArray();

        Assert.Empty(Revisions.Superseded(yearsApart));
    }

    private static ARevision[] Made(int count) =>
        [.. Enumerable.Range(0, count).Select(each => new ARevision(Start.AddHours(each), TheOwner))];

    private static Actor TheOwner => new() { Kind = CallerKind.Owner, Id = Guid.CreateVersion7() };
}
