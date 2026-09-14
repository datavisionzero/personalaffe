using System.Text;
using Personalaffe.Domain;
using Personalaffe.Domain.Tasks;

namespace Personalaffe.UnitTests;

/// <summary>
/// The rules of a task and its list: what a due date is, what completing is,
/// and what an order is made of.
/// </summary>
public sealed class PersonalTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private static readonly Guid AList = Guid.CreateVersion7(Now);

    [Fact]
    public void A_due_date_is_a_day_and_never_a_moment()
    {
        var task = PersonalTask.Captured(
            AList, "Milch holen", null, new DateOnly(2026, 9, 14), Positions.First, Now);

        // The one value in this product that is deliberately not an instant. A
        // task due on the fourteenth is due on the fourteenth wherever the owner
        // is standing, and there is no hour here for a timezone to move.
        Assert.Equal(new DateOnly(2026, 9, 14), task.DueOn);
        Assert.Equal(typeof(DateOnly?), typeof(PersonalTask).GetProperty(nameof(PersonalTask.DueOn))!.PropertyType);
    }

    [Fact]
    public void A_task_without_a_due_date_is_a_task()
    {
        Assert.Null(PersonalTask.Captured(AList, "Irgendwann", null, null, Positions.First, Now).DueOn);
    }

    [Fact]
    public void Completing_records_when_and_reopening_forgets_it()
    {
        var task = PersonalTask.Captured(AList, "Milch holen", null, null, Positions.First, Now);

        Assert.False(task.Completed);
        Assert.Null(task.CompletedAt);

        task.Change(AList, "Milch holen", null, null, completed: true, Positions.First, Later);

        Assert.True(task.Completed);
        Assert.Equal(Later, task.CompletedAt);

        task.Change(AList, "Milch holen", null, null, completed: false, Positions.First, Later.AddMinutes(1));

        Assert.False(task.Completed);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Ticking_a_box_that_is_already_ticked_does_not_move_the_moment()
    {
        var task = PersonalTask.Captured(AList, "Milch holen", null, null, Positions.First, Now);

        task.Change(AList, "Milch holen", null, null, completed: true, Positions.First, Later);

        // Something else about it changes, and the completion stays where it
        // was: "what did I finish this week" must not answer with whatever
        // somebody last touched.
        task.Change(AList, "Milch und Brot", null, null, completed: true, Positions.First, Later.AddHours(2));

        Assert.Equal(Later, task.CompletedAt);
    }

    [Fact]
    public void A_change_to_what_is_already_stored_is_not_a_change()
    {
        var task = PersonalTask.Captured(
            AList, "Milch holen", "am Markt", new DateOnly(2026, 9, 14), Positions.First, Now);

        Assert.False(task.Change(
            AList, "Milch holen", "am Markt", new DateOnly(2026, 9, 14), false, Positions.First, Later));
        Assert.Equal(Now, task.UpdatedAt);

        Assert.True(task.Change(
            AList, "Milch holen", "am Markt", new DateOnly(2026, 9, 15), false, Positions.First, Later));
        Assert.Equal(Later, task.UpdatedAt);
    }

    [Fact]
    public void One_write_carries_everything_a_task_is()
    {
        var task = PersonalTask.Captured(AList, "Milch holen", null, null, Positions.First, Now);
        var other = Guid.CreateVersion7(Now);

        Assert.True(task.Change(
            other, "Brot holen", "beim Bäcker", new DateOnly(2026, 9, 20), true, 42, Later));

        Assert.Equal(other, task.ListId);
        Assert.Equal("Brot holen", task.Title);
        Assert.Equal("beim Bäcker", task.Description);
        Assert.Equal(new DateOnly(2026, 9, 20), task.DueOn);
        Assert.True(task.Completed);
        Assert.Equal(42, task.Position);
    }

    [Fact]
    public void A_description_over_the_limit_is_refused_and_the_message_names_it()
    {
        var tooMuch = new string('x', PersonalTask.MaxDescriptionBytes + 1);

        var refusal = Assert.Throws<Refusal>(
            () => PersonalTask.Captured(AList, "Zu viel", tooMuch, null, Positions.First, Now));

        Assert.Equal(RefusalCode.Validation, refusal.Code);
        Assert.Contains(
            PersonalTask.MaxDescriptionBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refusal.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_description_limit_is_bytes_of_utf8_and_not_characters()
    {
        var justOver = string.Concat(
            Enumerable.Repeat("あ", (PersonalTask.MaxDescriptionBytes / 3) + 1));

        Assert.True(justOver.Length < PersonalTask.MaxDescriptionBytes);
        Assert.True(Encoding.UTF8.GetByteCount(justOver) > PersonalTask.MaxDescriptionBytes);
        Assert.Throws<Refusal>(() => PersonalTask.Captured(AList, "Zu viel", justOver, null, Positions.First, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zwei\nZeilen")]
    public void A_title_that_is_not_one_line_of_something_is_refused(string? asked)
    {
        Assert.Equal(
            RefusalCode.Validation,
            Assert.Throws<Refusal>(() => TaskTitle.Accepted(asked)).Code);
    }

    [Fact]
    public void A_list_is_named_and_renamed_and_nothing_else()
    {
        var list = TaskList.Named("  Einkauf  ", Now);

        Assert.Equal("Einkauf", list.Name);
        Assert.False(list.Rename("Einkauf", Later));
        Assert.Equal(Now, list.UpdatedAt);

        Assert.True(list.Rename("Einkaufen", Later));
        Assert.Equal(Later, list.UpdatedAt);
    }

    [Fact]
    public void Two_names_that_differ_only_in_case_are_one_name()
    {
        Assert.True(TaskTitle.Same("Einkauf", "einkauf"));
        Assert.False(TaskTitle.Same("Einkauf", "Einkäufe"));
    }
}
