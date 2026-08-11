using Fakturenn.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace Fakturenn.Modules.Identity.Domain;

/// <summary>
/// The application's user. Keys are UUID v7 for the same reason the rest of the
/// system uses them: random v4 keys fragment PostgreSQL B-tree indexes.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditable
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Set when an account exists but has not completed TOTP enrolment. A user in
    /// this state has authenticated by password and may reach only the enrolment
    /// page — see <c>EnrolmentGateMiddleware</c>.
    /// </summary>
    public bool MustEnrolTotp { get; set; }

    /// <summary>
    /// Set when somebody other than the user chose the current password: an
    /// administrator creating the account, or an operator running
    /// <c>--reset-password</c>. Forces a change at next sign-in so the credential
    /// stops being shared the moment it is first used.
    /// </summary>
    public bool MustChangePassword { get; set; }

    // IAuditable, filled by AuditSaveChangesInterceptor
    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset ModifiedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;
}
