using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// A named bundle of permissions. Deliberately not ASP.NET Core Identity's
/// <c>IdentityRole</c>: epic E02b adds an OrganizationId to <see cref="UserRole"/>,
/// and the stock join table has nowhere to put one.
/// </summary>
public sealed class Role : IAuditable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Marks a role the application itself depends on. <see cref="Persistence.RoleSeeder"/>
    /// sets it and re-syncs those roles against the permission catalogue; operator-created
    /// roles are never touched.
    /// <para>
    /// In E02a this is a marker and nothing more — <b>no code reads it</b>. It is not
    /// enforcement, and describing it as a guard would be false: there is no path in this
    /// epic that deletes a role or removes a permission, so nothing exists for it to
    /// refuse. E02b introduces role management and is where the marker starts being
    /// enforced.
    /// </para>
    /// </summary>
    public bool IsSystemRole { get; set; }

    // IAuditable, filled by AuditSaveChangesInterceptor
    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;
}
