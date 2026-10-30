using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.SharedKernel.Persistence;

// SharedKernel itself stays free of EF Core because every .Contracts project references it.
public static class AuditColumnConfiguration
{
    // One place configures the audit columns for every auditable entity, so a later entity
    // cannot arrive with a different column width by accident.
    public static ModelBuilder ConfigureAuditColumns(this ModelBuilder modelBuilder)
    {
        // Materialised first: modelBuilder.Entity(...) mutates the model that
        // GetEntityTypes() enumerates.
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

        return modelBuilder;
    }
}
