using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Infrastructure.DataProtection;

/// <summary>
/// Used only by <c>dotnet ef</c>. The connection string is never read at design
/// time because migrations are generated here, not applied.
/// </summary>
public sealed class DataProtectionDbContextFactory : IDesignTimeDbContextFactory<DataProtectionDbContext>
{
    public DataProtectionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
