using Fakturenn.SharedKernel;
using Fakturenn.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Organizations.Persistence;

public sealed class OrganizationsDbContext(DbContextOptions<OrganizationsDbContext> options)
    : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "organizations";

    // public Properties
    public DbSet<Organization> Organizations => Set<Organization>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Organization>(organization =>
        {
            organization.ToTable(table => table.HasCheckConstraint(
                "CK_Organizations_Singleton",
                $"\"Id\" = '{Organization.SingletonId}'"));
            organization.Property(o => o.LegalName).HasMaxLength(200);
            organization.Property(o => o.CountryCode).HasMaxLength(2);
            organization.Property(o => o.VatId).HasMaxLength(EuIdentifiers.MaxVatIdLength);
            organization.Property(o => o.Iban).HasMaxLength(34);
            organization.Property(o => o.Currency).HasMaxLength(3);
            organization.Property(o => o.TimeZoneId).HasMaxLength(64);
            organization.Property(o => o.NumberPrefix).HasMaxLength(9);
            organization.Property(o => o.IssueDateDefault).HasConversion<string>().HasMaxLength(32);
            organization.Property(o => o.NumberScope).HasConversion<string>().HasMaxLength(16);

            // No HasData: OrganizationSeeder inserts the row from --migrate. EF owns seed data,
            // so a changed default would come back as an UpdateData over the live row.
        });

        modelBuilder.ConfigureAuditColumns();
        base.OnModelCreating(modelBuilder);
    }
}
