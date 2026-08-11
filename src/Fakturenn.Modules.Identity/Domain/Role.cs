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
    /// A role the application itself depends on. System roles cannot be deleted and
    /// cannot have their permissions removed, so an instance cannot be locked out of
    /// its own administration through the user interface.
    /// </summary>
    public bool IsSystemRole { get; set; }

    // IAuditable, filled by AuditSaveChangesInterceptor
    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;
}
