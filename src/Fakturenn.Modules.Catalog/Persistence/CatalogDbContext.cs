using Fakturenn.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Catalog.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "catalog";

    // public Properties
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<CatalogItem>(item =>
        {
            item.Property(i => i.CatalogItemNumber).HasMaxLength(CatalogRules.MaxNumberLength);
            item.HasIndex(i => i.CatalogItemNumber).IsUnique();
            item.Property(i => i.Name).HasMaxLength(CatalogRules.MaxNameLength);
            item.Property(i => i.Unit).HasMaxLength(3);
            item.Property(i => i.Currency).HasMaxLength(3);
            item.Property(i => i.UnitPrice).HasPrecision(20, 6);
            item.Property(i => i.VatRate).HasPrecision(5, 2);
        });

        modelBuilder.ConfigureAuditColumns();
        base.OnModelCreating(modelBuilder);
    }
}
