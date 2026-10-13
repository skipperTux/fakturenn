using Fakturenn.Modules.Customers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Customers.Features;

public sealed class SaveCatalogItemReference(CustomersDbContext db)
{
    // public Methods
    /// <summary>Sets, replaces or — with an empty reference — removes the customer's number for an item.</summary>
    public async Task HandleAsync(Guid customerId, Guid catalogItemId, string reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        CustomerCatalogItemReference? existing = await db.CatalogItemReferences.SingleOrDefaultAsync(
            r => r.CustomerId == customerId && r.CatalogItemId == catalogItemId, cancellationToken);
        string trimmed = reference.Trim();

        if (trimmed.Length == 0)
        {
            if (existing is not null)
            {
                db.CatalogItemReferences.Remove(existing);
            }
        }
        else if (existing is null)
        {
            db.CatalogItemReferences.Add(new CustomerCatalogItemReference
            {
                CustomerId = customerId,
                CatalogItemId = catalogItemId,
                CustomerCatalogItemNumber = trimmed,
            });
        }
        else
        {
            existing.CustomerCatalogItemNumber = trimmed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
