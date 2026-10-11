using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed record DraftForm(
    Guid InvoiceId,
    Guid CustomerId,
    Guid? ProjectId,
    Guid CatalogItemId,
    decimal Quantity,
    DateOnly IssueDate,
    DateOnly ServicePeriodStart,
    DateOnly ServicePeriodEnd);

public sealed class SaveDraft(InvoicesDbContext db)
{
    // public Methods
    /// <summary>The aggregate refuses what the invoice's state forbids; this only persists.</summary>
    public async Task HandleAsync(DraftForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        Invoice invoice = await db.Invoices.Include(i => i.Lines)
            .SingleAsync(i => i.Id == form.InvoiceId, cancellationToken);

        invoice.Edit(form.CustomerId, form.ProjectId, form.IssueDate, form.ServicePeriodStart, form.ServicePeriodEnd);
        invoice.SetSingleLine(form.CatalogItemId, new Quantity(form.Quantity));
        await db.SaveChangesAsync(cancellationToken);
    }
}
