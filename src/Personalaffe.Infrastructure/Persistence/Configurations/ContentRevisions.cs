using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The columns every revision carries (<see cref="IRevision"/>), applied by the
/// module that keeps history in one line — the way
/// <see cref="RecoverableContent"/> is.
/// </summary>
/// <remarks>
/// The actor is three columns and no foreign key, for the reason
/// <see cref="Actor"/> gives: an access that has been revoked since still has
/// to be nameable in a history the owner is reading precisely to find out who
/// changed something.
/// </remarks>
public static class ContentRevisions
{
    public static EntityTypeBuilder<TRevision> IsARevision<TRevision>(
        this EntityTypeBuilder<TRevision> builder)
        where TRevision : class, IRevision
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<DateTimeOffset>(nameof(IRevision.At)).HasColumnName("at").IsRequired();

        builder.OwnsOne<Actor>(nameof(IRevision.By), actor =>
        {
            actor.Property(who => who.Kind)
                .HasColumnName("by_kind")
                .HasConversion(new SnakeCaseEnumConverter<CallerKind>());
            actor.Property(who => who.Id).HasColumnName("by_id");
            actor.Property(who => who.Name)
                .HasColumnName("by_name")
                .HasMaxLength(AgentAccess.NameMaxLength);
        });

        return builder;
    }
}
