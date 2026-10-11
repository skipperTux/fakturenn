using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// Assigns a role to a user. With one organization per instance no OrganizationId
/// column is planned (spec section 2); the table is our own because the roles are.
/// </summary>
public sealed class UserRole : IAuditable
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    // IAuditable, filled by AuditSaveChangesInterceptor
    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;
}
