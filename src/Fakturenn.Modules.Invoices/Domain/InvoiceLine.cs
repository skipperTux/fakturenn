using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// A line references its catalog item by id; price and rate are resolved at finalization
/// and stored here only as the outcome (spec section 5). Stage 1 uses exactly one line.
/// </summary>
public sealed class InvoiceLine : IAuditable
{
    // public Properties
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid CatalogItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? NetAmount { get; set; }
    public TaxCategory? TaxCategory { get; set; }
    public decimal? VatRate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
