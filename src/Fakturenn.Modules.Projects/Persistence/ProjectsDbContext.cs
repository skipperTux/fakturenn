using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.Modules.Projects.Persistence;

public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : DbContext(options)
{
    // public const Fields
    public const string SchemaName = "projects";

    // public Properties
    public DbSet<Project> Projects => Set<Project>();

    // protected Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Project>(project =>
        {
            project.Property(p => p.Reference).HasMaxLength(Project.MaxReferenceLength);
            project.HasIndex(p => new { p.CustomerId, p.Reference }).IsUnique();
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
