using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain.Tasks;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The task lists.
/// </summary>
/// <remarks>
/// No parent and no tree: a list is not in another list (<see cref="TaskList"/>).
/// This is the one content table in the product with nothing structural in it
/// at all.
/// </remarks>
public sealed class TaskListConfiguration : IEntityTypeConfiguration<TaskList>
{
    public void Configure(EntityTypeBuilder<TaskList> builder)
    {
        builder.ToTable("task_lists");

        builder.HasKey(list => list.Id).HasName("pk_task_lists");
        builder.Property(list => list.Id).HasColumnName("id");

        builder.Property(list => list.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(TaskTitle.MaxLength);

        builder.Property(list => list.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(list => list.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(list => list.Version);

        builder.IsRecoverable();
    }
}

/// <summary>
/// The tasks.
/// </summary>
/// <remarks>
/// <para>
/// <c>due_on</c> is a <c>date</c> and not a timestamp, which is the whole of
/// how this application avoids every timezone bug it could have had
/// (<see cref="PersonalTask"/>). Npgsql maps <see cref="DateOnly"/> to it
/// directly, so nothing in between has an opinion about what hour the
/// fourteenth starts at.
/// </para>
/// <para>
/// <c>position</c> is a double with room on either side of it, so that moving
/// one task changes one row (<see cref="Positions"/>). The index is on
/// <c>(list_id, position)</c>, which is the one question a listing asks.
/// </para>
/// </remarks>
public sealed class PersonalTaskConfiguration : IEntityTypeConfiguration<PersonalTask>
{
    public void Configure(EntityTypeBuilder<PersonalTask> builder)
    {
        builder.ToTable("tasks");

        builder.HasKey(task => task.Id).HasName("pk_tasks");
        builder.Property(task => task.Id).HasColumnName("id");

        builder.Property(task => task.ListId).HasColumnName("list_id").IsRequired();

        builder.Property(task => task.Title)
            .HasColumnName("title")
            .IsRequired()
            .HasMaxLength(TaskTitle.MaxLength);

        builder.Property(task => task.Description)
            .HasColumnName("description")
            .IsRequired()
            // No length in the column: the limit is bytes of UTF-8 and the
            // domain holds it.
            .HasColumnType("text");

        builder.Property(task => task.DueOn).HasColumnName("due_on").HasColumnType("date");

        builder.Property(task => task.CompletedAt).HasColumnName("completed_at");

        builder.Property(task => task.Position).HasColumnName("position").IsRequired();

        builder.Property(task => task.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(task => task.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(task => task.Completed);
        builder.Ignore(task => task.Version);

        builder.IsRecoverable();

        builder.HasIndex(task => new { task.ListId, task.Position })
            .HasDatabaseName("ix_tasks_list_id_position");
    }
}
