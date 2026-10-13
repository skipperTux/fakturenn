namespace Fakturenn.Modules.Catalog.Contracts;

public interface ICatalogItemProvider
{
    Task<CatalogItemSnapshot?> FindAsync(Guid catalogItemId, CancellationToken cancellationToken);
}
