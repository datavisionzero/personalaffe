using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The one place that declares schema (<c>docs/codebase.md</c>).
/// </summary>
/// <remarks>
/// EF Core owns every table and the migrations that apply themselves on
/// startup. What it declares is what something already stores rows in: the
/// owner, from PERSONAL-E2. The content safeguards are PERSONAL-E3's and the
/// four applications bring their own tables with them — a table invented here
/// before something stores rows in it would be a shape nobody has had to live
/// with.
/// </remarks>
public sealed class PersonalaffeDbContext(DbContextOptions<PersonalaffeDbContext> options) : DbContext(options)
{
    /// <summary>
    /// The sole human this instance belongs to (<c>CONTEXT.md</c>). One row,
    /// held to one by the unique index the configuration declares — there is no
    /// second human account and nothing here can make one.
    /// </summary>
    public DbSet<Owner> Owners => Set<Owner>();

    /// <summary>
    /// The signed-in browsers: server-side, so that revoked means revoked
    /// (<see cref="BrowserSession"/>).
    /// </summary>
    public DbSet<BrowserSession> BrowserSessions => Set<BrowserSession>();

    /// <summary>
    /// What gets the owner in when the authenticator is gone, one code at a
    /// time (<see cref="RecoveryCode"/>).
    /// </summary>
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // One IEntityTypeConfiguration per table, found rather than listed, so
        // that a new table is one file and not also a line here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PersonalaffeDbContext).Assembly);
    }
}
