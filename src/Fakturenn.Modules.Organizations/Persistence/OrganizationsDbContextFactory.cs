using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Organizations.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; the connection string is never opened.</summary>
public sealed class OrganizationsDbContextFactory : IDesignTimeDbContextFactory<OrganizationsDbContext>
{
    public OrganizationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
