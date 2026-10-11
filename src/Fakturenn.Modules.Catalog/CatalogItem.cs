using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Catalog;

public sealed class CatalogItem : IAuditable
{
    // public Properties
    public Guid Id { get; set; }
    public string CatalogItemNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal VatRate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
