namespace Fakturenn.SharedKernel;

/// <summary>
/// Row-level provenance: who created this row and who last changed it.
/// <para>
/// Implemented by every entity Fakturenn defines. The values are filled by
/// <c>AuditSaveChangesInterceptor</c>, so entity code never sets them by hand and
/// cannot forget to.
/// </para>
/// <para>
/// This is not the Audit module. MODULE-OWNERSHIP.md assigns an Audit module owning
/// AuditEvent and correlation metadata, which is an event log. This is a property of
/// each row.
/// </para>
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    string CreatedBy { get; set; }

    DateTimeOffset ModifiedAt { get; set; }

    string ModifiedBy { get; set; }
}
