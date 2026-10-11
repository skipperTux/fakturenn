using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Modules.Invoices.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fakturenn.Modules.Invoices.Features;

public sealed record FinalizationResult(string? Number, FinalizationRefusal? Refusal, InvoiceTotals? Totals)
{
    public bool Succeeded => Refusal is null;
}

/// <summary>
/// Spec section 5. One unit of work inside the execution strategy: the context uses
/// EnableRetryOnFailure, and EF requires a user transaction to be retried as a whole, so
/// every load happens inside the delegate and the change tracker is cleared at the start of
/// each attempt (review S2).
/// </summary>
public sealed class FinalizeInvoice(InvoicesDbContext db, InvoiceResolution resolution)
{
    // public Methods
    public Task<FinalizationResult> HandleAsync(Guid invoiceId, decimal confirmedGross, CancellationToken cancellationToken)
    {
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            (invoiceId, confirmedGross),
            async (_, state, token) => await AttemptAsync(state.invoiceId, state.confirmedGross, token),
            verifySucceeded: null,
            cancellationToken);
    }

    // private Methods
    private async Task<FinalizationResult> AttemptAsync(Guid invoiceId, decimal confirmedGross, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Step 1: lock the invoice; resolution re-checks its state under the lock. Two tabs, a
        // double click and a strategy retry after a commit-time failure all land here.
        await db.LockInvoiceAsync(invoiceId, cancellationToken);
        Invoice invoice = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId, cancellationToken);

        ResolvedInvoice resolved = await resolution.ResolveAsync(invoice, cancellationToken);
        if (resolved.Refusal is FinalizationRefusal refusal)
        {
            return new FinalizationResult(invoice.Number, refusal, null);
        }

        if (resolved.Totals!.Gross != confirmedGross)
        {
            return new FinalizationResult(invoice.Number, FinalizationRefusal.TotalsChanged, resolved.Totals);
        }

        // Step 6: allocate only when entering Numbered. A numbered invoice keeps its number.
        string number = invoice.Number ?? InvoiceNumberFormat.Format(
            resolved.Seller!.NumberScheme,
            invoice.IssueDate,
            await NumberAllocator.AllocateAsync(
                db,
                InvoiceNumberFormat.ScopeKey(resolved.Seller.NumberScheme.Scope, invoice.IssueDate),
                resolved.Seller.NumberScheme.NextNumber,
                cancellationToken));

        DateOnly dueDate = invoice.IssueDate.AddDays(resolved.Seller!.PaymentTermsDays);
        invoice.ApplyFinalization(number, dueDate, resolved.Totals, TaxCategory.S, resolved.Items[0]!.VatRate);

        // Step 7: serialize, hash, upsert one-to-one by invoice.
        byte[] content = InvoiceSnapshotSerializer.Serialize(InvoiceResolution.Document(invoice, number, dueDate, resolved));
        InvoiceSnapshotRecord? snapshot = await db.Snapshots.SingleOrDefaultAsync(s => s.InvoiceId == invoiceId, cancellationToken);
        if (snapshot is null)
        {
            snapshot = new InvoiceSnapshotRecord { InvoiceId = invoiceId };
            db.Snapshots.Add(snapshot);
        }

        snapshot.Content = content;
        snapshot.Sha256 = InvoiceSnapshotSerializer.Hash(content);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new FinalizationResult(number, null, resolved.Totals);
    }
}
