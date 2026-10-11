using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed class ListInvoices(InvoicesDbContext db)
{
    // public Methods
    public async Task<IReadOnlyList<InvoiceView>> HandleAsync(CancellationToken cancellationToken)
    {
        List<Invoice> invoices = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .OrderByDescending(i => i.IssueDate).ThenBy(i => i.Number)
            .ToListAsync(cancellationToken);
        return [.. invoices.Select(InvoiceView.From)];
    }
}
