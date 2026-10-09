namespace Fakturenn.Modules.Projects.Contracts;

/// <summary>A project belongs to exactly one customer; Reference is the customer's project number.</summary>
public sealed record ProjectSnapshot(Guid Id, Guid CustomerId, string Reference);
