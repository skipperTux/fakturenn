using System.Text.RegularExpressions;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Organizations;

/// <summary>The organization page's input, field for field.</summary>
public sealed record OrganizationForm(
    string LegalName,
    string CountryCode,
    string VatId,
    string Iban,
    string Currency,
    string TimeZoneId,
    int PaymentTermsDays,
    IssueDateDefault IssueDateDefault,
    string NumberPrefix,
    ResetScope NumberScope,
    int NumberPadding,
    long NumberNextValue)
{
    public static OrganizationForm Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            "Europe/Berlin", 14, IssueDateDefault.Today, string.Empty, ResetScope.PerYear, 4, 1);
}

public enum OrganizationError
{
    CountryInvalid,
    VatIdInvalid,
    IbanInvalid,
    CurrencyInvalid,
    TimeZoneInvalid,
    PaymentTermsInvalid,
    PrefixInvalid,
    PaddingInvalid,
    NextNumberInvalid,
    SchemeLocked,
    LegalNameTooLong,
}

/// <summary>
/// Field rules (spec section 10, review M3). Format only: an empty field is not an error
/// here — finalization refuses an incomplete seller with its own named reason.
/// VAT IDs are checked for format and country prefix only; VIES validation is optional
/// and lives in the backlog.
/// </summary>
public static partial class OrganizationRules
{
    // public const Fields
    /// <summary>Matches the legal name column's length.</summary>
    public const int MaxLegalNameLength = 200;

    // public static Methods
    public static IReadOnlyList<OrganizationError> Validate(OrganizationForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        List<OrganizationError> errors = [];

        if (form.LegalName.Trim().Length > MaxLegalNameLength)
        {
            errors.Add(OrganizationError.LegalNameTooLong);
        }

        if (form.CountryCode.Length > 0 && !EuIdentifiers.IsCountry(form.CountryCode))
        {
            errors.Add(OrganizationError.CountryInvalid);
        }

        if (form.VatId.Length > 0 && !EuIdentifiers.IsVatIdFor(form.CountryCode, form.VatId))
        {
            errors.Add(OrganizationError.VatIdInvalid);
        }

        if (form.Iban.Length > 0 && !IsIban(form.Iban))
        {
            errors.Add(OrganizationError.IbanInvalid);
        }

        if (form.Currency.Length > 0 && !CurrencyPattern().IsMatch(form.Currency))
        {
            errors.Add(OrganizationError.CurrencyInvalid);
        }

        if (!IsTimeZone(form.TimeZoneId))
        {
            errors.Add(OrganizationError.TimeZoneInvalid);
        }

        if (form.PaymentTermsDays is < 0 or > 365)
        {
            errors.Add(OrganizationError.PaymentTermsInvalid);
        }

        if (!PrefixPattern().IsMatch(form.NumberPrefix))
        {
            errors.Add(OrganizationError.PrefixInvalid);
        }

        if (form.NumberPadding is < 1 or > 9)
        {
            errors.Add(OrganizationError.PaddingInvalid);
        }

        if (form.NumberNextValue < 1)
        {
            errors.Add(OrganizationError.NextNumberInvalid);
        }

        return errors;
    }

    /// <summary>Spaces are tolerated on input and removed before storing.</summary>
    public static string NormalizeIban(string iban) =>
        iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    // private static Methods
    private static bool IsIban(string input)
    {
        string iban = NormalizeIban(input);
        if (!IbanPattern().IsMatch(iban))
        {
            return false;
        }

        // ISO 13616: move the first four characters to the end, map A..Z to 10..35, and the
        // whole number modulo 97 must be 1. Computed digit by digit to stay within a long.
        string rearranged = string.Concat(iban.AsSpan(4), iban.AsSpan(0, 4));
        long remainder = 0;
        foreach (char character in rearranged)
        {
            int value = char.IsDigit(character) ? character - '0' : character - 'A' + 10;
            remainder = ((remainder * (value < 10 ? 10 : 100)) + value) % 97;
        }

        return remainder == 1;
    }

    private static bool IsTimeZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IbanPattern();

    [GeneratedRegex("^[A-Z]{3}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex("^[A-Za-z0-9]{0,9}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}
