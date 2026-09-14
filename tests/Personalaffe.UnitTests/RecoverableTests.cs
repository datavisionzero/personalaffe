using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// Setting content aside rather than destroying it, and who the Trash says did
/// it.
/// </summary>
public sealed class RecoverableTests
{
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    private sealed class APage : IRecoverable
    {
        public DateTimeOffset? DeletedAt { get; set; }

        public Actor? DeletedBy { get; set; }
    }

    [Fact]
    public void Content_that_has_not_been_deleted_has_no_deletion_and_no_expiry()
    {
        var page = new APage();

        Assert.False(page.IsDeleted());
        Assert.Null(page.ExpiresAt(ThirtyDays));
        Assert.Throws<InvalidOperationException>(() => page.Gone("The page", ThirtyDays));
    }

    [Fact]
    public void Deleting_records_the_moment_and_expiry_follows_from_it()
    {
        var page = new APage();
        var at = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        page.Delete(Caller.Owner(Guid.CreateVersion7()), at);

        Assert.True(page.IsDeleted());
        Assert.Equal(at, page.DeletedAt);
        Assert.Equal(at + ThirtyDays, page.ExpiresAt(ThirtyDays));
    }

    [Fact]
    public void The_owner_is_the_whole_answer_and_has_no_name()
    {
        var page = new APage();

        page.Delete(Caller.Owner(Guid.CreateVersion7()), DateTimeOffset.UtcNow);

        Assert.Equal(CallerKind.Owner, page.DeletedBy?.Kind);
        Assert.Null(page.DeletedBy?.Name);
        Assert.Equal("the owner", page.DeletedBy?.Describe());
    }

    [Fact]
    public void An_agent_is_named_because_that_is_the_question_the_trash_answers()
    {
        var (access, _) = AgentAccess.Grant("the laptop agent", Permissions.Full, DateTimeOffset.UtcNow);
        var page = new APage();

        page.Delete(Caller.Agent(access), DateTimeOffset.UtcNow);

        Assert.Equal(CallerKind.Agent, page.DeletedBy?.Kind);
        Assert.Equal("the laptop agent", page.DeletedBy?.Name);
        Assert.Equal(access.Id, page.DeletedBy?.Id);
    }

    [Fact]
    public void Restoring_leaves_nothing_of_the_deletion_behind()
    {
        var page = new APage();

        page.Delete(Caller.Owner(Guid.CreateVersion7()), DateTimeOffset.UtcNow);
        page.Restore();

        Assert.False(page.IsDeleted());
        Assert.Null(page.DeletedAt);
        Assert.Null(page.DeletedBy);
    }

    [Fact]
    public void A_deleted_address_says_it_can_be_brought_back_and_by_when()
    {
        var at = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        var page = new APage();

        page.Delete(Caller.Owner(Guid.CreateVersion7()), at);

        var refusal = page.Gone("The page", ThirtyDays);

        Assert.Equal(RefusalCode.Deleted, refusal.Code);
        Assert.Equal(at, refusal.Extensions["deleted_at"]);
        Assert.Equal(at + ThirtyDays, refusal.Extensions["expires_at"]);
    }
}
