using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// The Invoices module owns this context and its migrations. No other module
/// may reference the entities it maps.
/// </summary>
public sealed class InvoicesDbContext(DbContextOptions<InvoicesDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "invoices";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        base.OnModelCreating(modelBuilder);
    }
}
