using Fakturenn.Modules.Catalog;
using Fakturenn.Modules.Customers;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations;
using Microsoft.Extensions.Localization;

namespace Fakturenn.Web.Components.Business;

// No discard arm, so a value added to one of these enums fails the build with CS8509 here
// instead of throwing on the page. CS8524 is what remains: it asks for a value outside the
// named ones, which no slice produces.
#pragma warning disable CS8524
/// <summary>
/// Every code to its message, each key a string literal so SharedResourceTests can see it.
/// A switch rather than Localizer[code]: a key built at run time is invisible to the scan
/// that proves both resource files are complete.
/// </summary>
internal static class RefusalMessages
{
    // public static Methods
    public static string For(IStringLocalizer<SharedResource> localizer, FinalizationRefusal refusal) => refusal switch
    {
        FinalizationRefusal.InvoiceLocked => localizer["Refusal_InvoiceLocked"],
        FinalizationRefusal.NoLine => localizer["Refusal_NoLine"],
        FinalizationRefusal.QuantityNotPositive => localizer["Refusal_QuantityNotPositive"],
        FinalizationRefusal.ServicePeriodInvalid => localizer["Refusal_ServicePeriodInvalid"],
        FinalizationRefusal.SellerIncomplete => localizer["Refusal_SellerIncomplete"],
        FinalizationRefusal.CustomerMissing => localizer["Refusal_CustomerMissing"],
        FinalizationRefusal.CustomerIncomplete => localizer["Refusal_CustomerIncomplete"],
        FinalizationRefusal.ProjectBelongsToAnotherCustomer => localizer["Refusal_ProjectBelongsToAnotherCustomer"],
        FinalizationRefusal.CatalogItemMissing => localizer["Refusal_CatalogItemMissing"],
        FinalizationRefusal.CurrencyMismatch => localizer["Refusal_CurrencyMismatch"],
        FinalizationRefusal.CrossBorderVatNotImplemented => localizer["Refusal_CrossBorderVatNotImplemented"],
        FinalizationRefusal.StandardRateMustBePositive => localizer["Refusal_StandardRateMustBePositive"],
        FinalizationRefusal.TotalsChanged => localizer["Refusal_TotalsChanged"],
    };

    public static string For(IStringLocalizer<SharedResource> localizer, InvoiceState state) => state switch
    {
        InvoiceState.Draft => localizer["Invoice_State_Draft"],
        InvoiceState.Numbered => localizer["Invoice_State_Numbered"],
        InvoiceState.Locked => localizer["Invoice_State_Locked"],
    };

    public static string For(IStringLocalizer<SharedResource> localizer, OrganizationError error) => error switch
    {
        OrganizationError.CountryInvalid => localizer["Organization_Error_CountryInvalid"],
        OrganizationError.VatIdInvalid => localizer["Organization_Error_VatIdInvalid"],
        OrganizationError.IbanInvalid => localizer["Organization_Error_IbanInvalid"],
        OrganizationError.CurrencyInvalid => localizer["Organization_Error_CurrencyInvalid"],
        OrganizationError.TimeZoneInvalid => localizer["Organization_Error_TimeZoneInvalid"],
        OrganizationError.PaymentTermsInvalid => localizer["Organization_Error_PaymentTermsInvalid"],
        OrganizationError.PrefixInvalid => localizer["Organization_Error_PrefixInvalid"],
        OrganizationError.PaddingInvalid => localizer["Organization_Error_PaddingInvalid"],
        OrganizationError.NextNumberInvalid => localizer["Organization_Error_NextNumberInvalid"],
        OrganizationError.SchemeLocked => localizer["Organization_Error_SchemeLocked"],
        OrganizationError.LegalNameTooLong => localizer["Organization_Error_LegalNameTooLong"],
    };

    public static string For(IStringLocalizer<SharedResource> localizer, CustomerError error) => error switch
    {
        CustomerError.LegalNameMissing => localizer["Customer_Error_LegalNameMissing"],
        CustomerError.CountryInvalid => localizer["Customer_Error_CountryInvalid"],
        CustomerError.VatIdInvalid => localizer["Customer_Error_VatIdInvalid"],
        CustomerError.CustomerNumberMissing => localizer["Customer_Error_CustomerNumberMissing"],
        CustomerError.CustomerNumberTaken => localizer["Customer_Error_CustomerNumberTaken"],
        CustomerError.LegalNameTooLong => localizer["Customer_Error_LegalNameTooLong"],
        CustomerError.CustomerNumberTooLong => localizer["Customer_Error_CustomerNumberTooLong"],
    };

    public static string For(IStringLocalizer<SharedResource> localizer, CatalogError error) => error switch
    {
        CatalogError.NumberMissing => localizer["Catalog_Error_NumberMissing"],
        CatalogError.NumberTaken => localizer["Catalog_Error_NumberTaken"],
        CatalogError.NameMissing => localizer["Catalog_Error_NameMissing"],
        CatalogError.UnitInvalid => localizer["Catalog_Error_UnitInvalid"],
        CatalogError.PriceNegative => localizer["Catalog_Error_PriceNegative"],
        CatalogError.CurrencyInvalid => localizer["Catalog_Error_CurrencyInvalid"],
        CatalogError.VatRateInvalid => localizer["Catalog_Error_VatRateInvalid"],
        CatalogError.NumberTooLong => localizer["Catalog_Error_NumberTooLong"],
        CatalogError.NameTooLong => localizer["Catalog_Error_NameTooLong"],
        CatalogError.PriceOutOfRange => localizer["Catalog_Error_PriceOutOfRange"],
    };
}
#pragma warning restore CS8524
