namespace Fakturenn.Modules.Customers.Contracts;

public interface ICustomerProvider
{
    Task<CustomerSnapshot?> FindAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// The customer's own name for a catalog item — the CustomerCatalogItemNumber — or null.
    /// Per customer, which is why it lives here and not on the catalog item (review C7).
    /// </summary>
    Task<string?> FindCatalogItemReferenceAsync(Guid customerId, Guid catalogItemId, CancellationToken cancellationToken);
}
