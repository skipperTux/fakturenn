using Fakturenn.SharedKernel;
using Fakturenn.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Persistence;

public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "customers";

    // public Properties
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerCatalogItemReference> CatalogItemReferences => Set<CustomerCatalogItemReference>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Customer>(customer =>
        {
            customer.Property(c => c.LegalName).HasMaxLength(CustomerRules.MaxLegalNameLength);
            customer.Property(c => c.CountryCode).HasMaxLength(2);
            customer.Property(c => c.VatId).HasMaxLength(EuIdentifiers.MaxVatIdLength);
            customer.Property(c => c.CustomerNumber).HasMaxLength(CustomerRules.MaxCustomerNumberLength);
            customer.HasIndex(c => c.CustomerNumber).IsUnique();
        });

        modelBuilder.Entity<CustomerCatalogItemReference>(reference =>
        {
            reference.HasKey(r => new { r.CustomerId, r.CatalogItemId });
            reference.Property(r => r.CustomerCatalogItemNumber).HasMaxLength(CustomerRules.MaxCustomerCatalogItemNumberLength);
            reference.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.ConfigureAuditColumns();
        base.OnModelCreating(modelBuilder);
    }
}
