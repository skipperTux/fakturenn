using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
            item.Property(i => i.UnitPrice).HasPrecision(18, 4);
            item.Property(i => i.VatRate).HasPrecision(5, 2);
        });

        ConfigureAuditColumns(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // private static Methods
    private static void ConfigureAuditColumns(ModelBuilder modelBuilder)
    {
        List<IMutableEntityType> auditable = [.. modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType))];

        foreach (IMutableEntityType entityType in auditable)
        {
            modelBuilder.Entity(entityType.ClrType, entity =>
            {
                entity.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.CreatedAt)).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedAt)).IsRequired();
            });
        }
    }
}
