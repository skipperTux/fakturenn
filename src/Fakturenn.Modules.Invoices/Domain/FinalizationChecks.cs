using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// What "complete" means at finalization (spec section 5 step 3, review C6). The seller VAT
/// ID is mandatory while it is the only seller tax identifier modelled (EN 16931 BR-S-02).
/// </summary>
public static class FinalizationChecks
{
    // public static Methods
    public static FinalizationRefusal? Check(
        SellerSnapshot seller,
        CustomerSnapshot? customer,
        ProjectSnapshot? project,
        Guid invoiceCustomerId,
        IReadOnlyList<CatalogItemSnapshot?> items)
    {
        ArgumentNullException.ThrowIfNull(seller);
        ArgumentNullException.ThrowIfNull(items);

        string[] sellerFields = [seller.LegalName, seller.CountryCode, seller.VatId, seller.Iban, seller.Currency, seller.TimeZoneId];
        if (sellerFields.Any(string.IsNullOrWhiteSpace))
        {
            return FinalizationRefusal.SellerIncomplete;
        }

        if (customer is null)
        {
            return FinalizationRefusal.CustomerMissing;
        }

        string[] customerFields = [customer.LegalName, customer.CountryCode, customer.CustomerNumber];
        if (customerFields.Any(string.IsNullOrWhiteSpace))
        {
            return FinalizationRefusal.CustomerIncomplete;
        }

        if (project is not null && project.CustomerId != invoiceCustomerId)
        {
            return FinalizationRefusal.ProjectBelongsToAnotherCustomer;
        }

        return items.Any(item => item is null) ? FinalizationRefusal.CatalogItemMissing : null;
    }
}
