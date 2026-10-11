namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>Rule-free on purpose: a draft holds 0 while the user types and credit notes may need negatives; finalization refuses what it must.</summary>
public readonly record struct Quantity(decimal Value);
