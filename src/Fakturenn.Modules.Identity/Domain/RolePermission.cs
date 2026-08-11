using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// Grants one permission to one role. <see cref="Permission"/> holds a string that
/// must match a constant in <c>Permissions</c>; a startup check rejects any value
/// that does not.
/// </summary>
public sealed class RolePermission : IAuditable
{
    public Guid RoleId { get; set; }

    public string Permission { get; set; } = string.Empty;

    // IAuditable, filled by AuditSaveChangesInterceptor
    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;
}
