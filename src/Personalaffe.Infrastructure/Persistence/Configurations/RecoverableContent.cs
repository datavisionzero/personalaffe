using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The columns and the filter every piece of lasting content carries
/// (<see cref="IRecoverable"/>), applied by the module's own configuration in
/// one line.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The filter is the point.</strong> A convention that says "remember
/// to exclude deleted rows" is a convention that will be forgotten in the
/// twelfth query somebody writes, and the row it lets through is content the
/// owner believes they deleted. A query filter is applied by EF to every read
/// of the type, including the ones nobody thought about, and the one place that
/// wants the deleted rows — the Trash — says <c>IgnoreQueryFilters</c> out
/// loud.
/// </para>
/// <para>
/// It is an extension method rather than a base configuration class because
/// applications are folders that own their tables (<c>docs/codebase.md</c>).
/// Knowledge's configuration says what a page is and then calls this; it does
/// not inherit from something that half-describes it.
/// </para>
/// </remarks>
public static class RecoverableContent
{
    /// <summary>
    /// Maps <c>deleted_at</c> and the three columns of the actor, filters the
    /// deleted rows out of every ordinary read, and indexes what the purge
    /// sweeps.
    /// </summary>
    public static EntityTypeBuilder<TContent> IsRecoverable<TContent>(this EntityTypeBuilder<TContent> builder)
        where TContent : class, IRecoverable
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<DateTimeOffset?>(nameof(IRecoverable.DeletedAt)).HasColumnName("deleted_at");

        // Three columns and no foreign key: who deleted something is a copy of
        // what the caller was called at the time, and an access that has been
        // revoked since still has to be nameable (Actor).
        builder.OwnsOne<Actor>(nameof(IRecoverable.DeletedBy), actor =>
        {
            actor.Property(who => who.Kind)
                .HasColumnName("deleted_by_kind")
                .HasConversion(new SnakeCaseEnumConverter<CallerKind>());
            actor.Property(who => who.Id).HasColumnName("deleted_by_id");
            actor.Property(who => who.Name)
                .HasColumnName("deleted_by_name")
                .HasMaxLength(AgentAccess.NameMaxLength);
        });

        // Written as EF.Property rather than as a member access, because the
        // member is the interface's and the property is the entity's: this is
        // the spelling that names the mapped property whatever the type calls
        // it.
        builder.HasQueryFilter(content =>
            EF.Property<DateTimeOffset?>(content, nameof(IRecoverable.DeletedAt)) == null);

        // What the purge asks for — everything past its retention — and what
        // the Trash lists. Partial, because the rows it is about are the few
        // that are deleted and not the many that are not.
        builder.HasIndex(nameof(IRecoverable.DeletedAt)).HasFilter("deleted_at is not null");

        return builder;
    }
}
