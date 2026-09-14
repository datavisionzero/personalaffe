using System.Globalization;
using System.Text;

namespace Personalaffe.Domain.Tasks;

/// <summary>
/// A personal commitment that is open or completed, with an optional
/// description and due date (<c>CONTEXT.md</c>, Task).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The word is <em>task</em>, and the type carries a prefix for the
/// reason <see cref="WorkspaceApplication"/> and <c>StoredFile</c> do</strong>:
/// it is a fact about C# and not about the product.
/// <c>System.Threading.Tasks.Task</c> is in scope in every file in this
/// codebase, and a type called <c>Task</c> beside it would make every
/// asynchronous signature in the application a question about which one is
/// meant.
/// </para>
/// <para>
/// <strong>A due date is a date and never a moment.</strong> It is the one
/// value in this product that is deliberately not an instant: a task due on the
/// fourteenth is due on the fourteenth wherever the owner is standing, and an
/// instant would be due on the thirteenth for somebody who flew west. Every
/// timezone bug this application could have had is this one, and this is where
/// it is not had.
/// </para>
/// <para>
/// <strong>Completion is a moment and not a boolean.</strong> "Open or
/// completed" is the state; <em>when</em> is what a dashboard of "what did I
/// finish this week" needs (PERSONAL-E9), and a boolean would have to be
/// widened by a migration the first time anybody asked for that.
/// </para>
/// </remarks>
public sealed class PersonalTask : IRecoverable
{
    /// <summary>
    /// How much description one task holds, in bytes of UTF-8.
    /// </summary>
    /// <remarks>
    /// 64 KiB, the Scratchpad's number, because it answers the same question:
    /// far more than anybody writes beside a commitment and far less than a row
    /// somebody has to be told about. What needs more than that is a knowledge
    /// page, and a task can say so with a link to one.
    /// </remarks>
    public const int MaxDescriptionBytes = 64 * 1024;

    private PersonalTask()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>The list it is in.</summary>
    public Guid ListId { get; private set; }

    /// <summary>What the owner has to do (<see cref="TaskTitle"/>).</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Whatever else there is to say about it, as Markdown. May be empty.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The day it is due, or nothing. A date, and never a moment.</summary>
    public DateOnly? DueOn { get; private set; }

    /// <summary>When it was completed, or nothing while it is open.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Where it sits among the others (<see cref="Positions"/>).</summary>
    public double Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When it was last changed, and the version a write replaces.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Actor? DeletedBy { get; set; }

    /// <summary>Whether it is done.</summary>
    public bool Completed => CompletedAt is not null;

    /// <summary>The version a guarded write has to be holding.</summary>
    public ContentVersion Version => ContentVersion.Of(UpdatedAt);

    /// <summary>A task captured now, at <paramref name="position"/>.</summary>
    /// <exception cref="Refusal"><c>validation</c>: not a title, or too much description.</exception>
    public static PersonalTask Captured(
        Guid list, string? title, string? description, DateOnly? dueOn, double position, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ListId = list,
            Title = TaskTitle.Accepted(title),
            Description = Accepted(description),
            DueOn = dueOn,
            Position = position,
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>
    /// Changes anything about it — the title, the description, the due date, the
    /// list, whether it is done, where it sits — and says whether that changed
    /// anything.
    /// </summary>
    /// <remarks>
    /// <strong>One write and not six.</strong> A task has more fields than
    /// anything else in this workspace and is exactly where a second address
    /// per field would start to look reasonable: `POST .../complete`,
    /// `PUT .../due`, `POST .../move`. Each would be another place the guard has
    /// to be got right, and the owner who ticks a box and changes the date has
    /// made one change either way.
    /// </remarks>
    /// <exception cref="Refusal"><c>validation</c>: not a title, or too much description.</exception>
    public bool Change(
        Guid list,
        string? title,
        string? description,
        DateOnly? dueOn,
        bool completed,
        double position,
        DateTimeOffset now)
    {
        var wantedTitle = TaskTitle.Accepted(title);
        var wantedDescription = Accepted(description);

        var same = ListId == list
            && string.Equals(Title, wantedTitle, StringComparison.Ordinal)
            && string.Equals(Description, wantedDescription, StringComparison.Ordinal)
            && DueOn == dueOn
            && Completed == completed
            && Position.Equals(position);

        if (same)
        {
            return false;
        }

        ListId = list;
        Title = wantedTitle;
        Description = wantedDescription;
        DueOn = dueOn;
        Position = position;

        // Ticking a box that is already ticked is not a completion, and must not
        // move the moment it happened: "what did I finish this week" would
        // otherwise answer with whatever somebody last touched.
        if (completed != Completed)
        {
            CompletedAt = completed ? now : null;
        }

        UpdatedAt = now;

        return true;
    }

    /// <summary>Puts it somewhere else in its list, and nothing else.</summary>
    /// <remarks>
    /// Used where the module renumbers a list that has run out of room between
    /// two positions; an ordinary move goes through <see cref="Change"/> with
    /// the rest of what the task is.
    /// </remarks>
    public void PutAt(double position, DateTimeOffset now)
    {
        Position = position;
        UpdatedAt = now;
    }

    /// <summary>Moves the version on for a change the module made itself.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    private static string Accepted(string? description)
    {
        var kept = description ?? string.Empty;
        var bytes = Encoding.UTF8.GetByteCount(kept);

        return bytes <= MaxDescriptionBytes
            ? kept
            : throw Refusal.Validation(
                "description",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A description is at most {MaxDescriptionBytes} bytes of UTF-8, and this one is "
                    + $"{bytes}. What needs more than that is a knowledge page, and a task can link to one."));
    }
}
