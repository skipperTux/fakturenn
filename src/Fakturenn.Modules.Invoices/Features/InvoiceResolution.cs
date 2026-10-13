using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Snapshots;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Features;

public sealed record ResolvedInvoice(
    FinalizationRefusal? Refusal,
    SellerSnapshot? Seller,
    CustomerSnapshot? Customer,
    ProjectSnapshot? Project,
    IReadOnlyList<CatalogItemSnapshot?> Items,
    IReadOnlyList<string?> CustomerItemNumbers,
    InvoiceTotals? Totals);

/// <summary>
/// Spec section 5's state check and steps 2 to 5, shared by preview and finalization so the
/// refusals and totals a user confirms come from the code that numbers them (review M12).
/// Reads go through the other modules' contracts and their own contexts — outside the invoice
/// transaction, accepted in the spec.
/// </summary>
public sealed class InvoiceResolution(
    ISellerProvider sellers,
    ICustomerProvider customers,
    IProjectProvider projects,
    ICatalogItemProvider catalog)
{
    // public Methods
    public async Task<ResolvedInvoice> ResolveAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        FinalizationRefusal? invoiceRefusal = invoice.State == InvoiceState.Locked ? FinalizationRefusal.InvoiceLocked
            : invoice.Lines.Count == 0 ? FinalizationRefusal.NoLine
            : invoice.Lines.Any(line => line.Quantity.Value <= 0m) ? FinalizationRefusal.QuantityNotPositive
            : invoice.ServicePeriodEnd < invoice.ServicePeriodStart ? FinalizationRefusal.ServicePeriodInvalid
            : null;
        if (invoiceRefusal is not null)
        {
            return Refused(invoiceRefusal.Value);
        }

        SellerSnapshot seller = await sellers.GetAsync(cancellationToken);
        CustomerSnapshot? customer = await customers.FindAsync(invoice.CustomerId, cancellationToken);
        ProjectSnapshot? project = invoice.ProjectId is Guid projectId ? await projects.FindAsync(projectId, cancellationToken) : null;

        List<CatalogItemSnapshot?> items = [];
        List<string?> references = [];
        foreach (InvoiceLine line in invoice.Lines)
        {
            items.Add(await catalog.FindAsync(line.CatalogItemId, cancellationToken));
            references.Add(await customers.FindCatalogItemReferenceAsync(invoice.CustomerId, line.CatalogItemId, cancellationToken));
        }

        FinalizationRefusal? refusal = FinalizationChecks.Check(seller, customer, project, invoice.CustomerId, items);
        if (refusal is not null)
        {
            return Refused(refusal.Value);
        }

        foreach (CatalogItemSnapshot item in items.OfType<CatalogItemSnapshot>())
        {
            FinalizationRefusal? tax = TaxDecision.Decide(seller.CountryCode, customer!.CountryCode, item.VatRate, seller.Currency, item.Currency);
            if (tax is not null)
            {
                return Refused(tax.Value);
            }
        }

        InvoiceTotals totals = InvoiceCalculator.Calculate(
            [.. invoice.Lines.Select((line, index) => new PricedLine(line.Quantity, items[index]!.UnitPrice, new Percentage(items[index]!.VatRate), TaxCategory.S))],
            seller.Currency);

        return new ResolvedInvoice(null, seller, customer, project, items, references, totals);
    }

    // internal static Methods
    internal static InvoiceSnapshotDocument Document(Invoice invoice, string number, DateOnly dueDate, ResolvedInvoice resolved)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(resolved);

        List<SnapshotLine> lines = [.. invoice.Lines.Select((line, index) => new SnapshotLine(
            resolved.Items[index]!, resolved.CustomerItemNumbers[index], line.Quantity.Value,
            resolved.Totals!.LineNets[index], TaxCategory.S, resolved.Items[index]!.VatRate))];

        return new InvoiceSnapshotDocument(
            invoice.Id, number, invoice.IssueDate, invoice.ServicePeriodStart, invoice.ServicePeriodEnd, dueDate,
            resolved.Seller!, resolved.Customer!, resolved.Project, lines, resolved.Totals!.Subtotals,
            resolved.Totals.Net, resolved.Totals.Vat, resolved.Totals.Gross, resolved.Totals.Currency);
    }

    // private static Methods
    private static ResolvedInvoice Refused(FinalizationRefusal refusal) => new(refusal, null, null, null, [], [], null);
}
