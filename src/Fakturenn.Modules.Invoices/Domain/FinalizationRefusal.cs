namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>Every reason finalization says no. Each has a localized message (Task 8).</summary>
public enum FinalizationRefusal
{
    InvoiceLocked,
    NoLine,
    QuantityNotPositive,
    ServicePeriodInvalid,
    SellerIncomplete,
    CustomerMissing,
    CustomerIncomplete,
    ProjectBelongsToAnotherCustomer,
    CatalogItemMissing,
    CurrencyMismatch,
    CrossBorderVatNotImplemented,
    StandardRateMustBePositive,
    TotalsChanged,
}
