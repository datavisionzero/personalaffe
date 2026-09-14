using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Personalaffe.Domain;
using Personalaffe.Infrastructure.Persistence.Configurations;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// A table the tests own, so that PERSONAL-E3's conventions can be proved
/// against a real Postgres before there is any content to prove them on.
/// </summary>
/// <remarks>
/// <para>
/// PERSONAL-E3 lands before Scratchpad, Knowledge, Tasks and Files, and its
/// deliverable is the conventions those four inherit: the concurrency token,
/// the recoverable deletion, the actor, the restore into a tree, the revision.
/// A convention nobody has applied is a convention nobody has tested, so this
/// is a module in every respect that matters — a table, a configuration, a
/// store — that exists only inside the test project.
/// </para>
/// <para>
/// <strong>Nothing here is in <c>src/</c>.</strong> No migration carries it, no
/// endpoint reaches it, and it is not in <c>docs/api/openapi.json</c>: an
/// instance somebody installs has no such table. What is under test is the
/// production code it calls — <c>GuardedSave</c>, <c>ContentVersion</c>,
/// <c>Deletion</c>, <c>Restoration</c> — applied exactly the way a real module
/// will apply it.
/// </para>
/// </remarks>
internal sealed class Thing : IRecoverable
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>A tree, because two of the four applications have one.</summary>
    public Guid? ParentId { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The version a write holds on to (<see cref="ContentVersion"/>).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Actor? DeletedBy { get; set; }

    public ContentVersion Version => ContentVersion.Of(UpdatedAt);
}

/// <summary>
/// A previous version of a thing's content — what a module that keeps history
/// writes beside its own table (<see cref="Revisions"/>).
/// </summary>
internal sealed class ThingRevision : IRevision
{
    public Guid Id { get; set; }

    public Guid ThingId { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    public Actor By { get; set; } = null!;
}

internal sealed class ThingRevisionConfiguration : IEntityTypeConfiguration<ThingRevision>
{
    public void Configure(EntityTypeBuilder<ThingRevision> builder)
    {
        builder.ToTable("thing_revisions");
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.ThingId).HasColumnName("thing_id");
        builder.Property(revision => revision.Content).HasColumnName("content");

        // The line a module with history inherits.
        builder.IsARevision();

        // Revisions belong to the thing they are of: removing it for good
        // removes them, and nothing outlives the page it is a version of.
        builder.HasOne<Thing>()
            .WithMany()
            .HasForeignKey(revision => revision.ThingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ThingConfiguration : IEntityTypeConfiguration<Thing>
{
    public void Configure(EntityTypeBuilder<Thing> builder)
    {
        builder.ToTable("things");
        builder.HasKey(thing => thing.Id);
        builder.Property(thing => thing.Name).HasMaxLength(200);

        // The two lines a module inherits. Declaring the timestamp a
        // concurrency token is what makes EF write `where … and updated_at = …`,
        // and what `GuardedSave` turns into the product's refusal.
        builder.Property(thing => thing.UpdatedAt).IsConcurrencyToken();

        // The other line: the deletion columns, the filter that keeps deleted
        // rows out of every read nobody thought about, and the index the purge
        // sweeps.
        builder.IsRecoverable();
    }
}

internal sealed class ProvingGround(DbContextOptions<ProvingGround> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    public DbSet<ThingRevision> ThingRevisions => Set<ThingRevision>();

    /// <summary>A database with the proving ground in it, and a way back to it.</summary>
    public static async Task<Func<ProvingGround>> PreparedAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        ProvingGround Open() => new(
            new DbContextOptionsBuilder<ProvingGround>().UseNpgsql(connectionString).Options);

        await using (var context = Open())
        {
            await context.Database.EnsureCreatedAsync();
        }

        return Open;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ThingConfiguration());
        modelBuilder.ApplyConfiguration(new ThingRevisionConfiguration());
    }
}
