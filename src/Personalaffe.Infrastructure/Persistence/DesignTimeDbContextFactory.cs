using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Personalaffe.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the Api project.
/// </summary>
/// <remarks>
/// The connection string below is never connected to: adding or scripting a
/// migration reads the model and not the database. Keeping the tooling out of
/// the composition root is what makes a migration something that can be added
/// without a running instance anywhere:
/// <c>dotnet ef migrations add Name --project src/Personalaffe.Infrastructure</c>.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PersonalaffeDbContext>
{
    public PersonalaffeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PersonalaffeDbContext>()
            .UseNpgsql("Host=design-time;Database=personalaffe;Username=personalaffe")
            .Options;

        return new PersonalaffeDbContext(options);
    }
}
