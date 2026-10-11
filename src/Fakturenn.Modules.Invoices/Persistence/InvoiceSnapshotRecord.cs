using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// The snapshot bytes as written, and their SHA-256. One per invoice, overwritten while the
/// invoice is Numbered, never after it locks. The hash proves corruption, not tampering:
/// whoever can rewrite Content can rewrite Sha256 in the same row (spec section 4).
/// </summary>
public sealed class InvoiceSnapshotRecord : IAuditable
{
    public Guid InvoiceId { get; set; }
    public byte[] Content { get; set; } = [];
    public byte[] Sha256 { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
