using Fakturenn.SharedKernel;

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
public static class CustomerRules
{
    // public const Fields
    public const int MaxLegalNameLength = 200;
    public const int MaxCustomerNumberLength = 50;

    /// <summary>The column's length. SaveCatalogItemReference has no length rule of its own.</summary>
    public const int MaxCustomerCatalogItemNumberLength = 50;

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

        if (!EuIdentifiers.IsCountry(form.CountryCode))
        {
            errors.Add(CustomerError.CountryInvalid);
        }

        if (form.VatId.Length > 0 && !EuIdentifiers.IsVatIdFor(form.CountryCode, form.VatId))
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
}
