namespace Fakturenn.Modules.Organizations.Contracts;

/// <summary>What resets the invoice counter. The date part of the number follows from it.</summary>
public enum ResetScope
{
    PerDay,
    PerMonth,
    PerYear,
    Continuous,
}
