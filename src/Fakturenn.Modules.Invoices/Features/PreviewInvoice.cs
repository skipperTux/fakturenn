using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed class PreviewInvoice(InvoicesDbContext db, InvoiceResolution resolution)
{
    // public Methods
    public async Task<FinalizationResult> HandleAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        Invoice invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .SingleAsync(i => i.Id == invoiceId, cancellationToken);

        ResolvedInvoice resolved = await resolution.ResolveAsync(invoice, cancellationToken);
        return new FinalizationResult(invoice.Number, resolved.Refusal, resolved.Totals);
    }
}
