using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Customers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Features;

public sealed class CustomerProvider(CustomersDbContext db) : ICustomerProvider
{
    // public Methods
    public async Task<CustomerSnapshot?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerSnapshot(c.Id, c.LegalName, c.CountryCode, c.VatId, c.CustomerNumber))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<string?> FindCatalogItemReferenceAsync(Guid customerId, Guid catalogItemId, CancellationToken cancellationToken) =>
        await db.CatalogItemReferences.AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.CatalogItemId == catalogItemId)
            .Select(r => r.CustomerCatalogItemNumber)
            .SingleOrDefaultAsync(cancellationToken);
}
