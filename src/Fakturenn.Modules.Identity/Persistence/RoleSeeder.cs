using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Seeds the system roles the application itself depends on.
/// <para>
/// Called from the <c>--migrate</c> entrypoint, never at application startup:
/// startup seeding races on the unique role-name index when more than one replica
/// starts together, and <c>--migrate</c> already runs exactly once by design.
/// </para>
/// </summary>
public static class RoleSeeder
{
    public const string AdministratorRoleName = "Administrator";

    /// <summary>
    /// Ensures the Administrator system role exists and holds every declared
    /// permission.
    /// <para>
    /// This is a <b>re-sync, not create-if-absent</b>. An installation upgraded to a
    /// version that defines a new permission constant gains the grant on the next
    /// <c>--migrate</c>. <c>PermissionCatalogValidator</c> catches stored permissions
    /// the code does not define; nothing but this catches permissions the code
    /// defines and the database lacks.
    /// </para>
    /// <para>
    /// Idempotent: re-running grants nothing twice and creates no second role.
    /// Operator-created roles are never touched.
    /// </para>
    /// </summary>
    public static async Task SeedAsync(IdentityDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Role? administrator = await context.Roles
            .SingleOrDefaultAsync(role => role.Name == AdministratorRoleName, cancellationToken);

        if (administrator is null)
        {
            administrator = new Role
            {
                Id = Guid.CreateVersion7(),
                Name = AdministratorRoleName,
                Description = "Full system administration.",
                IsSystemRole = true,
            };
            context.Roles.Add(administrator);
        }

        List<string> existing = await context.RolePermissions
            .Where(rolePermission => rolePermission.RoleId == administrator.Id)
            .Select(rolePermission => rolePermission.Permission)
            .ToListAsync(cancellationToken);

        foreach (string permission in Permissions.All.Except(existing, StringComparer.Ordinal))
        {
            context.RolePermissions.Add(new RolePermission
            {
                RoleId = administrator.Id,
                Permission = permission,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
