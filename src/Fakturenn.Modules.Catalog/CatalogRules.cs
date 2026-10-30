using System.Text.RegularExpressions;

namespace Fakturenn.Modules.Catalog;

public sealed record CatalogItemForm(Guid? Id, string CatalogItemNumber, string Name, string Unit, decimal UnitPrice, string Currency, decimal VatRate);

public sealed record CatalogSaveResult(Guid? Id, IReadOnlyList<CatalogError> Errors);

public enum CatalogError
{
    NumberMissing,
    NumberTaken,
    NameMissing,
    UnitInvalid,
    PriceNegative,
    CurrencyInvalid,
    VatRateInvalid,
    NumberTooLong,
    NameTooLong,
    PriceOutOfRange,
}

public static partial class CatalogRules
{
    // public const Fields
    public const int MaxNumberLength = 50;
    public const int MaxNameLength = 200;

    // private const Fields
    // numeric(20,6) holds up to 10^14 - 0.000001; PostgreSQL would round further decimals silently.
    private const int PriceScale = 6;
    private const decimal PriceCeiling = 100_000_000_000_000m;
    private const int VatRateScale = 2;

    // public static Methods
    public static IReadOnlyList<CatalogError> Validate(CatalogItemForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        List<CatalogError> errors = [];
        if (string.IsNullOrWhiteSpace(form.CatalogItemNumber))
        {
            errors.Add(CatalogError.NumberMissing);
        }
        else if (form.CatalogItemNumber.Trim().Length > MaxNumberLength)
        {
            errors.Add(CatalogError.NumberTooLong);
        }

        if (string.IsNullOrWhiteSpace(form.Name))
        {
            errors.Add(CatalogError.NameMissing);
        }
        else if (form.Name.Trim().Length > MaxNameLength)
        {
            errors.Add(CatalogError.NameTooLong);
        }

        if (!UnitPattern().IsMatch(form.Unit))
        {
            errors.Add(CatalogError.UnitInvalid);
        }

        if (form.UnitPrice < 0m)
        {
            errors.Add(CatalogError.PriceNegative);
        }

        if (form.UnitPrice >= PriceCeiling || decimal.Round(form.UnitPrice, PriceScale) != form.UnitPrice)
        {
            errors.Add(CatalogError.PriceOutOfRange);
        }

        if (!CurrencyPattern().IsMatch(form.Currency))
        {
            errors.Add(CatalogError.CurrencyInvalid);
        }

        if (form.VatRate is < 0m or > 100m || decimal.Round(form.VatRate, VatRateScale) != form.VatRate)
        {
            errors.Add(CatalogError.VatRateInvalid);
        }

        return errors;
    }

    // private static Methods
    [GeneratedRegex(@"^[A-Z0-9]{1,3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex UnitPattern();

    [GeneratedRegex(@"^[A-Z]{3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
