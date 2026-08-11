using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// Assigns a role to a user. Epic E02b adds an OrganizationId here, which is why
/// this is a table of our own rather than Identity's AspNetUserRoles.
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
