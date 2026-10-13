using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Projects;

/// <summary>Belongs to one customer, by id only — Customers is another module (spec section 3, review C8).</summary>
public sealed class Project : IAuditable
{
    // public const Fields
    /// <summary>The reference column's length. SaveProject has no length rule of its own.</summary>
    public const int MaxReferenceLength = 50;

    // public Properties
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
