using Fakturenn.Modules.Catalog.Contracts;
using Fakturenn.Modules.Customers.Contracts;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations.Contracts;
using Fakturenn.Modules.Projects.Contracts;

namespace Fakturenn.Modules.Invoices.Snapshots;

public sealed record SnapshotLine(
    CatalogItemSnapshot CatalogItem,
    string? CustomerCatalogItemNumber,
    decimal Quantity,
    decimal NetAmount,
    TaxCategory TaxCategory,
    decimal VatRate);

/// <summary>
/// Everything the artifacts are produced from, resolved at finalization. The number scheme
/// travels inside <see cref="Seller"/>, so the snapshot records which scheme produced its
/// number (spec section 6).
/// </summary>
public sealed record InvoiceSnapshotDocument(
    Guid InvoiceId,
    string Number,
    DateOnly IssueDate,
    DateOnly ServicePeriodStart,
    DateOnly ServicePeriodEnd,
    DateOnly DueDate,
    SellerSnapshot Seller,
    CustomerSnapshot Customer,
    ProjectSnapshot? Project,
    IReadOnlyList<SnapshotLine> Lines,
    IReadOnlyList<TaxSubtotal> TaxSubtotals,
    decimal Net,
    decimal Vat,
    decimal Gross,
    string Currency);
