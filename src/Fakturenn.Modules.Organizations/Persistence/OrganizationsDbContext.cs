using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
            organization.Property(o => o.VatId).HasMaxLength(14);
            organization.Property(o => o.Iban).HasMaxLength(34);
            organization.Property(o => o.Currency).HasMaxLength(3);
            organization.Property(o => o.TimeZoneId).HasMaxLength(64);
            organization.Property(o => o.NumberPrefix).HasMaxLength(9);
            organization.Property(o => o.IssueDateDefault).HasConversion<string>().HasMaxLength(32);
            organization.Property(o => o.NumberScope).HasConversion<string>().HasMaxLength(16);

            // No HasData: OrganizationSeeder inserts the row from --migrate. EF owns seed data,
            // so a changed default would come back as an UpdateData over the live row.
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
