namespace Fakturenn.Modules.Invoices.Contracts;

/// <summary>Whether any invoice number has ever been allocated. Locks the organization's number scheme.</summary>
public interface INumberingStatus
{
    Task<bool> AnyNumberAllocatedAsync(CancellationToken cancellationToken);
}
