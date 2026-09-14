using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// The version a write holds on to, and the one thing that has to be true of
/// it: a value that has been through Postgres is still the value the client
/// read.
/// </summary>
public sealed class ContentVersionTests
{
    [Fact]
    public void A_version_is_the_moment_the_object_last_changed()
    {
        var moment = new DateTimeOffset(2026, 9, 14, 8, 30, 0, TimeSpan.Zero);

        Assert.Equal(moment, ContentVersion.Of(moment).UpdatedAt);
    }

    [Fact]
    public void A_version_is_in_utc_whatever_offset_it_arrived_in()
    {
        var berlin = new DateTimeOffset(2026, 9, 14, 10, 30, 0, TimeSpan.FromHours(2));

        var version = ContentVersion.Of(berlin);

        Assert.Equal(TimeSpan.Zero, version.UpdatedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 8, 30, 0, TimeSpan.Zero), version.UpdatedAt);
    }

    [Fact]
    public void Everything_below_a_microsecond_is_dropped_because_postgres_drops_it()
    {
        // Seven digits of fraction: .NET counts in hundreds of nanoseconds and
        // the database does not. Without the truncation a value written by
        // TimeProvider and read back would never match itself, and the guard
        // would refuse every write in the product.
        var precise = new DateTimeOffset(2026, 9, 14, 8, 30, 0, TimeSpan.Zero).AddTicks(1_234_567);
        var stored = new DateTimeOffset(2026, 9, 14, 8, 30, 0, TimeSpan.Zero).AddTicks(1_234_560);

        Assert.Equal(stored, ContentVersion.Of(precise).UpdatedAt);
        Assert.True(ContentVersion.Of(precise).Matches(ContentVersion.Of(stored)));
    }

    [Fact]
    public void A_version_matches_only_itself()
    {
        var moment = new DateTimeOffset(2026, 9, 14, 8, 30, 0, TimeSpan.Zero);
        var version = ContentVersion.Of(moment);

        Assert.True(version.Matches(ContentVersion.Of(moment)));
        Assert.False(version.Matches(ContentVersion.Of(moment.AddMicroseconds(1))));
        Assert.False(version.Matches(ContentVersion.Of(moment.AddMicroseconds(-1))));
    }

    [Fact]
    public void Holding_nothing_is_not_holding_the_current_version()
    {
        var version = ContentVersion.Of(DateTimeOffset.UtcNow);

        Assert.False(version.Matches(null!));
    }
}
