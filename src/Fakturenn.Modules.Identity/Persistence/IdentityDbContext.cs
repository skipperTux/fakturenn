using Fakturenn.Modules.Identity.Domain;
using Fakturenn.SharedKernel;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// The Identity module owns this context and its migrations.
/// <para>
/// Derives from <see cref="IdentityUserContext{TUser, TKey}"/> rather than
/// <c>IdentityDbContext</c> on purpose: the former creates users, claims, logins and
/// tokens but no role tables. Roles live in <see cref="Roles"/> and
/// <see cref="UserRoles"/> instead, because epic E02b needs an OrganizationId on the
/// user-role join and AspNetUserRoles has nowhere to put one. Running both role
/// systems side by side later would be worse than not adopting the stock one now.
/// </para>
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public const string SchemaName = "identity";

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(SchemaName);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
            user.Property(u => u.CreatedAt).IsRequired();
        });

        builder.Entity<Role>(role =>
        {
            role.HasKey(r => r.Id);
            role.Property(r => r.Name).HasMaxLength(128).IsRequired();
            role.Property(r => r.Description).HasMaxLength(512);
            role.HasIndex(r => r.Name).IsUnique();
        });

        builder.Entity<RolePermission>(rolePermission =>
        {
            rolePermission.HasKey(rp => new { rp.RoleId, rp.Permission });
            rolePermission.Property(rp => rp.Permission).HasMaxLength(128).IsRequired();
        });

        builder.Entity<UserRole>(userRole =>
        {
            userRole.HasKey(ur => new { ur.UserId, ur.RoleId });
        });

        // One place configures the audit columns for every auditable entity, so a
        // later entity cannot arrive with a different column width by accident.
        foreach (IMutableEntityType entityType in builder.Model.GetEntityTypes()
                     .Where(type => typeof(IAuditable).IsAssignableFrom(type.ClrType)))
        {
            builder.Entity(entityType.ClrType, entity =>
            {
                entity.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedBy)).HasMaxLength(256).IsRequired();
                entity.Property(nameof(IAuditable.CreatedAt)).IsRequired();
                entity.Property(nameof(IAuditable.ModifiedAt)).IsRequired();
            });
        }
    }
}
