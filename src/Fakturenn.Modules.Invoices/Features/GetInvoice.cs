using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.Modules.Invoices.Features;

public sealed record InvoiceView(
    Guid Id,
    InvoiceState State,
    string? Number,
    Guid CustomerId,
    Guid? ProjectId,
    Guid? CatalogItemId,
    decimal Quantity,
    DateOnly IssueDate,
    DateOnly ServicePeriodStart,
    DateOnly ServicePeriodEnd,
    DateOnly? DueDate,
    decimal? Net,
    decimal? Vat,
    decimal? Gross,
    string? Currency,
    TaxCategory? TaxCategory,
    decimal? VatRate)
{
    public static InvoiceView From(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        InvoiceLine? line = invoice.Lines.SingleOrDefault();
        return new InvoiceView(
            invoice.Id, invoice.State, invoice.Number, invoice.CustomerId, invoice.ProjectId,
            line?.CatalogItemId, line?.Quantity.Value ?? 0m, invoice.IssueDate, invoice.ServicePeriodStart,
            invoice.ServicePeriodEnd, invoice.DueDate, invoice.NetAmount, invoice.VatAmount,
            invoice.GrossAmount, invoice.Currency, invoice.TaxCategory, invoice.VatRate);
    }
}

public sealed class GetInvoice(InvoicesDbContext db)
{
    // public Methods
    public async Task<InvoiceView?> HandleAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        Invoice? invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        return invoice is null ? null : InvoiceView.From(invoice);
    }
}
