namespace Fakturenn.Modules.Organizations.Contracts;

/// <summary>
/// The organization as finalization reads it. Fields may be empty: the seeded row starts
/// empty, and finalization — not this record — decides what "complete" means.
/// </summary>
public sealed record SellerSnapshot(
    string LegalName,
    string CountryCode,
    string VatId,
    string Iban,
    string Currency,
    string TimeZoneId,
    int PaymentTermsDays,
    IssueDateDefault IssueDateDefault,
    NumberScheme NumberScheme);
