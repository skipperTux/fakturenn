namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>Stage 1 implements category S and refuses everything else, by name (spec section 7).</summary>
public static class TaxDecision
{
    public static FinalizationRefusal? Decide(
        string sellerCountry,
        string customerCountry,
        decimal vatRate,
        string sellerCurrency,
        string itemCurrency)
    {
        if (!string.Equals(sellerCountry, customerCountry, StringComparison.Ordinal))
        {
            return FinalizationRefusal.CrossBorderVatNotImplemented;
        }

        if (vatRate <= 0m)
        {
            return FinalizationRefusal.StandardRateMustBePositive;
        }

        if (!string.Equals(sellerCurrency, itemCurrency, StringComparison.Ordinal))
        {
            return FinalizationRefusal.CurrencyMismatch;
        }

        return null;
    }
}
