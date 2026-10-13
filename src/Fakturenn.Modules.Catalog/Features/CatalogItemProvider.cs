using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Catalog.Features;

public sealed class CatalogItemProvider(CatalogDbContext db) : ICatalogItemProvider
{
    // public Methods
    public async Task<CatalogItemSnapshot?> FindAsync(Guid catalogItemId, CancellationToken cancellationToken) =>
        await db.CatalogItems.AsNoTracking()
            .Where(i => i.Id == catalogItemId)
            .Select(i => new CatalogItemSnapshot(i.Id, i.CatalogItemNumber, i.Name, i.Unit, i.UnitPrice, i.Currency, i.VatRate))
            .SingleOrDefaultAsync(cancellationToken);
}
