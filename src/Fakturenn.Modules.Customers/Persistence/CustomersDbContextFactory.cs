using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Customers.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; the connection string is never opened.</summary>
public sealed class CustomersDbContextFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CustomersDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
