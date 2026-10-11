using Fakturenn.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Catalog.Features;

public sealed class SaveCatalogItem(CatalogDbContext db)
{
    // public Methods
    public async Task<CatalogSaveResult> HandleAsync(CatalogItemForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        CatalogItemForm normalized = form with
        {
            CatalogItemNumber = form.CatalogItemNumber.Trim(),
            Name = form.Name.Trim(),
            Unit = form.Unit.Trim().ToUpperInvariant(),
            Currency = form.Currency.Trim().ToUpperInvariant(),
        };

        List<CatalogError> errors = [.. CatalogRules.Validate(normalized)];
        bool taken = await db.CatalogItems.AnyAsync(
            i => i.CatalogItemNumber == normalized.CatalogItemNumber && i.Id != normalized.Id, cancellationToken);
        if (taken)
        {
            errors.Add(CatalogError.NumberTaken);
        }

        if (errors.Count > 0)
        {
            return new CatalogSaveResult(null, errors);
        }

        CatalogItem item = normalized.Id is Guid id
            ? await db.CatalogItems.SingleAsync(i => i.Id == id, cancellationToken)
            : db.CatalogItems.Add(new CatalogItem { Id = Guid.CreateVersion7() }).Entity;

        item.CatalogItemNumber = normalized.CatalogItemNumber;
        item.Name = normalized.Name;
        item.Unit = normalized.Unit;
        item.UnitPrice = normalized.UnitPrice;
        item.Currency = normalized.Currency;
        item.VatRate = normalized.VatRate;

        await db.SaveChangesAsync(cancellationToken);
        return new CatalogSaveResult(item.Id, []);
    }
}
