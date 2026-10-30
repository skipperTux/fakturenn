using Fakturenn.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

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

        modelBuilder.ConfigureAuditColumns();
        base.OnModelCreating(modelBuilder);
    }
}
