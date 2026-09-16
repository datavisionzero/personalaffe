using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain;
using Personalaffe.Domain.Dashboard;
using Personalaffe.Domain.Files;
using Personalaffe.Domain.Knowledge;
using Personalaffe.Domain.Scratchpad;
using Personalaffe.Domain.Tasks;
using Personalaffe.Domain.Weather;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The one place that declares schema (<c>docs/codebase.md</c>).
/// </summary>
/// <remarks>
/// EF Core owns every table and the migrations that apply themselves on
/// startup. What it declares is what something already stores rows in: the
/// owner and what gets them in, from PERSONAL-E2, the application switch from
/// PERSONAL-E4, the Scratchpad from PERSONAL-E5, the Files application from
/// PERSONAL-E6, Knowledge from PERSONAL-E7, Tasks from PERSONAL-E8 — which is
/// all four of them — and the home page's tiles and its weather place from PERSONAL-E9. Every table
/// here is one something stores rows in; a table invented before that would be
/// a shape nobody has had to live with.
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

    /// <summary>
    /// The named, revocable authorizations the owner has handed out
    /// (<see cref="AgentAccess"/>). Not users: there is one human account and
    /// nothing here can become a second.
    /// </summary>
    public DbSet<AgentAccess> AgentAccess => Set<AgentAccess>();

    /// <summary>
    /// Which of the four applications this workspace has switched on
    /// (<see cref="ApplicationState"/>). Four rows, seeded by the migration
    /// that creates the table.
    /// </summary>
    public DbSet<ApplicationState> Applications => Set<ApplicationState>();

    /// <summary>
    /// Which compact views the owner has on their home page
    /// (<see cref="TileState"/>). One row per tile, seeded by the migration
    /// that creates the table.
    /// </summary>
    public DbSet<TileState> DashboardTiles => Set<TileState>();

    /// <summary>
    /// Where the owner wants the weather for (<see cref="Domain.Weather.WeatherPlace"/>).
    /// One row, held to one by the check constraint the configuration declares,
    /// and nowhere until the owner says.
    /// </summary>
    public DbSet<WeatherPlace> WeatherPlace => Set<WeatherPlace>();

    /// <summary>
    /// Whether this instance is being held still while a backup takes the
    /// database and the file volume as of one moment
    /// (<see cref="Domain.MaintenancePause"/>). One row, held to one by the
    /// check constraint the configuration declares, and holding nothing until
    /// a backup says otherwise.
    /// </summary>
    public DbSet<MaintenancePause> MaintenancePause => Set<MaintenancePause>();

    /// <summary>
    /// The temporary plain text the owner keeps for cross-device use
    /// (<see cref="ScratchpadEntry"/>). The one table here with no
    /// <c>deleted_at</c>: what is deleted from it is destroyed.
    /// </summary>
    public DbSet<ScratchpadEntry> ScratchpadEntries => Set<ScratchpadEntry>();

    /// <summary>
    /// The owner's files — their metadata (<see cref="StoredFile"/>). The bytes
    /// are on the storage volume and never in a column here (VISION §9).
    /// </summary>
    public DbSet<StoredFile> Files => Set<StoredFile>();

    /// <summary>
    /// The tree the files are in (<see cref="Folder"/>). The root is not a row:
    /// a folder with no parent is at the top.
    /// </summary>
    public DbSet<Folder> Folders => Set<Folder>();

    /// <summary>
    /// The owner's lasting knowledge, as Markdown in a tree (<see cref="Page"/>).
    /// </summary>
    public DbSet<Page> Pages => Set<Page>();

    /// <summary>
    /// What those pages used to say (<see cref="PageRevision"/>). A revision has
    /// no state of its own: it goes wherever its page goes.
    /// </summary>
    public DbSet<PageRevision> PageRevisions => Set<PageRevision>();

    /// <summary>
    /// The owner's named task lists (<see cref="TaskList"/>). Flat: a list is
    /// not in another list.
    /// </summary>
    public DbSet<TaskList> TaskLists => Set<TaskList>();

    /// <summary>
    /// What is in them (<see cref="PersonalTask"/>). <c>due_on</c> is a date and
    /// never a moment, which is the whole of how this application has no
    /// timezone bug.
    /// </summary>
    public DbSet<PersonalTask> Tasks => Set<PersonalTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // One IEntityTypeConfiguration per table, found rather than listed, so
        // that a new table is one file and not also a line here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PersonalaffeDbContext).Assembly);
    }
}
