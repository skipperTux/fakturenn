using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Customers;

/// <summary>
/// The customer's own number for a catalog item — "SI-9001" is Example Client GmbH's name
/// for DEV-BACKEND. Holds the catalog item's id only; Catalog is another module.
/// </summary>
public sealed class CustomerCatalogItemReference : IAuditable
{
    // public Properties
    public Guid CustomerId { get; set; }
    public Guid CatalogItemId { get; set; }
    public string CustomerCatalogItemNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
