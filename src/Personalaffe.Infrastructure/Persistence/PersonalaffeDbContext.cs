using Microsoft.EntityFrameworkCore;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// The one place that declares schema (<c>docs/codebase.md</c>).
/// </summary>
/// <remarks>
/// EF Core owns every table and the migrations that apply themselves on
/// startup. It declares none yet: the owner is PERSONAL-E2's, the content
/// safeguards PERSONAL-E3's, and the four applications bring their own tables
/// with them. A table invented here before something stores rows in it would be
/// a shape nobody has had to live with.
/// </remarks>
public sealed class PersonalaffeDbContext(DbContextOptions<PersonalaffeDbContext> options) : DbContext(options);
