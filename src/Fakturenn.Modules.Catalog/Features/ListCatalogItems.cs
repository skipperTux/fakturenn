using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Catalog.Features;

public sealed class ListCatalogItems(CatalogDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<CatalogItemSnapshot>> HandleAsync(CancellationToken cancellationToken) =>
        await db.CatalogItems.AsNoTracking()
            .OrderBy(i => i.CatalogItemNumber)
            .Select(i => new CatalogItemSnapshot(i.Id, i.CatalogItemNumber, i.Name, i.Unit, i.UnitPrice, i.Currency, i.VatRate))
            .ToListAsync(cancellationToken);
}
