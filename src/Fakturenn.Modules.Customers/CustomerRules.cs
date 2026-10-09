using System.Globalization;
using System.Text.RegularExpressions;

namespace Fakturenn.Modules.Customers;

public sealed record CustomerForm(Guid? Id, string LegalName, string CountryCode, string VatId, string CustomerNumber);

public sealed record SaveResult(Guid? Id, IReadOnlyList<CustomerError> Errors);

public enum CustomerError
{
    LegalNameMissing,
    CountryInvalid,
    VatIdInvalid,
    CustomerNumberMissing,
    CustomerNumberTaken,
    LegalNameTooLong,
    CustomerNumberTooLong,
}

/// <summary>Required fields and formats. VAT ID format only — VIES is optional (spec section 7).</summary>
public static partial class CustomerRules
{
    // public const Fields
    public const int MaxLegalNameLength = 200;
    public const int MaxCustomerNumberLength = 50;

    // public static Methods
    public static IReadOnlyList<CustomerError> Validate(CustomerForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        List<CustomerError> errors = [];

        if (string.IsNullOrWhiteSpace(form.LegalName))
        {
            errors.Add(CustomerError.LegalNameMissing);
        }
        else if (form.LegalName.Trim().Length > MaxLegalNameLength)
        {
            errors.Add(CustomerError.LegalNameTooLong);
        }

        if (!IsCountry(form.CountryCode))
        {
            errors.Add(CustomerError.CountryInvalid);
        }

        if (form.VatId.Length > 0 && !IsVatIdFor(form.CountryCode, form.VatId))
        {
            errors.Add(CustomerError.VatIdInvalid);
        }

        if (string.IsNullOrWhiteSpace(form.CustomerNumber))
        {
            errors.Add(CustomerError.CustomerNumberMissing);
        }
        else if (form.CustomerNumber.Trim().Length > MaxCustomerNumberLength)
        {
            errors.Add(CustomerError.CustomerNumberTooLong);
        }

        return errors;
    }

    // private static Methods
    private static bool IsCountry(string code)
    {
        if (!CountryPattern().IsMatch(code))
        {
            return false;
        }

        try
        {
            return string.Equals(new RegionInfo(code).TwoLetterISORegionName, code, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsVatIdFor(string country, string vatId)
    {
        Match match = VatIdPattern().Match(vatId);
        if (!match.Success)
        {
            return false;
        }

        string prefix = match.Groups["country"].Value;
        return string.Equals(prefix, country, StringComparison.Ordinal) || (prefix == "EL" && country == "GR");
    }

    [GeneratedRegex("^[A-Z]{2}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();

    [GeneratedRegex("^(?<country>[A-Z]{2})[A-Z0-9+*]{2,12}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VatIdPattern();
}
