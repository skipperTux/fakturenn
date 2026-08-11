namespace Fakturenn.SharedKernel;

/// <summary>
/// The decisions the audit interceptor makes, as a pure function, so they are
/// testable without a database or a request pipeline.
/// </summary>
public static class AuditStamp
{
    public const string SystemUser = "system";

    /// <summary>
    /// Provenance for a newly added row. Values already present are preserved: a
    /// seeder or an import knows the real provenance, and overwriting it would
    /// replace a fact with the identity of whoever ran the import.
    /// </summary>
    public static (DateTimeOffset CreatedAt, string CreatedBy) ForAdded(
        DateTimeOffset existingCreatedAt,
        string? existingCreatedBy,
        DateTimeOffset now,
        string user)
    {
        DateTimeOffset createdAt = existingCreatedAt == default ? now : existingCreatedAt;
        string createdBy = string.IsNullOrWhiteSpace(existingCreatedBy) ? user : existingCreatedBy;

        return (createdAt, createdBy);
    }

    public static string ResolveUser(string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? SystemUser : userName;
}
