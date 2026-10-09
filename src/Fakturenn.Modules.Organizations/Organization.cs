using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Organizations;

/// <summary>
/// The seller. Exactly one row per instance, inserted by the module's first migration under
/// <see cref="SingletonId"/> and pinned there by a check constraint (spec section 2, review C2).
/// </summary>
public sealed class Organization : IAuditable
{
    // public static readonly Fields
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    // public Properties
    public Guid Id { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string VatId { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Europe/Berlin";
    public int PaymentTermsDays { get; set; } = 14;
    public IssueDateDefault IssueDateDefault { get; set; }
    public string NumberPrefix { get; set; } = string.Empty;
    public ResetScope NumberScope { get; set; } = ResetScope.PerYear;
    public int NumberPadding { get; set; } = 4;
    public long NumberNextValue { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
