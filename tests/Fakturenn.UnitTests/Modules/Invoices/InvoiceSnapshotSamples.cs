using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Invoices.Snapshots;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.Modules.Invoices;

internal static class InvoiceSnapshotSamples
{
    public static InvoiceSnapshotDocument WalkingSkeleton()
    {
        Guid customerId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(new Quantity(8m), 100m, new Percentage(19m), TaxCategory.S)], "EUR");
        return new InvoiceSnapshotDocument(
            Guid.Parse("01900000-0000-7000-8000-000000000002"),
            "R2608311",
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 9, 14),
            new SellerSnapshot("Example Consulting", "DE", "DE123456789", "DE89370400440532013000", "EUR", "Europe/Berlin", 14,
                IssueDateDefault.LastDayOfPreviousMonth, new NumberScheme("R", ResetScope.PerDay, 1, 1)),
            new CustomerSnapshot(customerId, "Example Client GmbH", "DE", null, "C-4711"),
            new ProjectSnapshot(Guid.Parse("01900000-0000-7000-8000-000000000003"), customerId, "PRJ-2026-083"),
            [new SnapshotLine(
                new CatalogItemSnapshot(Guid.Parse("01900000-0000-7000-8000-000000000004"), "DEV-BACKEND", "Backend development", "HUR", 100m, "EUR", 19m),
                "SI-9001", 8m, 800.00m, TaxCategory.S, 19m)],
            totals.Subtotals,
            totals.Net,
            totals.Vat,
            totals.Gross,
            "EUR");
    }
}
