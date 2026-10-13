using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// A line references its catalog item by id; price and rate are resolved at finalization
/// and stored here only as the outcome (spec section 5). Stage 1 uses exactly one line.
/// </summary>
public sealed class InvoiceLine : IAuditable
{
    public Guid Id { get; internal set; }
    public Guid InvoiceId { get; internal set; }
    public Guid CatalogItemId { get; internal set; }
    public Quantity Quantity { get; internal set; }
    public decimal? NetAmount { get; internal set; }
    public TaxCategory? TaxCategory { get; internal set; }
    public decimal? VatRate { get; internal set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
