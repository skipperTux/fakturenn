using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Projects.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; the connection string is never opened.</summary>
public sealed class ProjectsDbContextFactory : IDesignTimeDbContextFactory<ProjectsDbContext>
{
    public ProjectsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
