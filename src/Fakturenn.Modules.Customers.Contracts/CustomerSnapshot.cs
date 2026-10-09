namespace Fakturenn.Modules.Customers.Contracts;

/// <summary>A customer as finalization reads it. VatId is null for a private individual.</summary>
public sealed record CustomerSnapshot(Guid Id, string LegalName, string CountryCode, string? VatId, string CustomerNumber);
