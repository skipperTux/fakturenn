using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

/// <summary>
/// The aggregate refuses what the invoice's state forbids; this only persists. It loads under
/// finalization's row lock, so an edit is never applied to the state a finalization in flight
/// is replacing (review I1). Same retry shape as <see cref="FinalizeInvoice"/>.
/// </summary>
public sealed class SaveDraft(InvoicesDbContext db)
{
    // public Methods
    public Task HandleAsync(DraftForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(token => AttemptAsync(form, token), cancellationToken);
    }

    // private Methods
    private async Task AttemptAsync(DraftForm form, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.LockInvoiceAsync(form.InvoiceId, cancellationToken);
        Invoice invoice = await db.Invoices.Include(i => i.Lines)
            .SingleAsync(i => i.Id == form.InvoiceId, cancellationToken);

        invoice.Edit(form.CustomerId, form.ProjectId, form.IssueDate, form.ServicePeriodStart, form.ServicePeriodEnd);
        invoice.SetSingleLine(form.CatalogItemId, new Quantity(form.Quantity));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
