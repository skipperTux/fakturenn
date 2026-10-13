using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Customers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Features;

public sealed class ListCustomers(CustomersDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<CustomerSnapshot>> HandleAsync(CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking()
            .OrderBy(c => c.CustomerNumber)
            .Select(c => new CustomerSnapshot(c.Id, c.LegalName, c.CountryCode, c.VatId, c.CustomerNumber))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> ReferencesAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.CatalogItemReferences.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .ToDictionaryAsync(r => r.CatalogItemId, r => r.CustomerCatalogItemNumber, cancellationToken);
}
