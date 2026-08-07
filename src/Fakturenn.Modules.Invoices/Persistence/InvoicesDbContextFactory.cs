using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. The connection string is never read at
/// design time because migrations are generated, not applied, here.
/// </summary>
public sealed class InvoicesDbContextFactory : IDesignTimeDbContextFactory<InvoicesDbContext>
{
    public InvoicesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseNpgsql("Host=localhost;Database=fakturenn;Username=fakturenn;Password=design-time-only")
            .Options);
}
