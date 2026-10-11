namespace Fakturenn.Modules.Catalog.Contracts;

/// <summary>
/// A catalog item as finalization reads it. Unit is a UN/ECE Recommendation 20 code
/// ("HUR" is an hour); VatRate is in whole percent. No item type: nothing in Stage 1 reads it.
/// </summary>
public sealed record CatalogItemSnapshot(
    Guid Id,
    string CatalogItemNumber,
    string Name,
    string Unit,
    decimal UnitPrice,
    string Currency,
    decimal VatRate);
