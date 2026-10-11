namespace Fakturenn.Modules.Invoices.Persistence;

/// <summary>
/// The snapshot bytes as written, and their SHA-256. One per invoice, overwritten while the
/// invoice is Numbered, never after it locks. The hash proves corruption, not tampering:
/// whoever can rewrite Content can rewrite Sha256 in the same row (spec section 4).
/// </summary>
public sealed class InvoiceSnapshotRecord
{
    public Guid InvoiceId { get; set; }
    public byte[] Content { get; set; } = [];
    public byte[] Sha256 { get; set; } = [];
}
