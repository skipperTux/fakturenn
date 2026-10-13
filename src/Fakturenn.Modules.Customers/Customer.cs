using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Customers;

/// <summary>
/// Only what Stage 1 reads. Delivery email, e-invoice profile, PDF template and signing
/// policy arrive with the stage that reads each (spec section 3, review M11).
/// </summary>
public sealed class Customer : IAuditable
{
    // public Properties
    public Guid Id { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string? VatId { get; set; }
    public string CustomerNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
