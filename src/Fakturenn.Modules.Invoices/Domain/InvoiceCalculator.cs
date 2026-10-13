using Fakturenn.SharedKernel;

namespace Fakturenn.Modules.Invoices.Domain;

public sealed record PricedLine(Quantity Quantity, decimal UnitPrice, Percentage VatRate, TaxCategory Category);

public sealed record TaxSubtotal(TaxCategory Category, decimal Rate, decimal TaxableAmount, decimal TaxAmount);

public sealed record InvoiceTotals(
    decimal Net,
    decimal Vat,
    decimal Gross,
    string Currency,
    IReadOnlyList<decimal> LineNets,
    IReadOnlyList<TaxSubtotal> Subtotals);

/// <summary>
/// Line nets rounded to cents, then VAT per (category, rate) on the summed nets, rounded
/// once — as EN 16931 computes it (spec section 5 step 5, review M13). Rounding is the
/// shared kernel's commercial rounding, away from zero (review M4).
/// </summary>
public static class InvoiceCalculator
{
    public static InvoiceTotals Calculate(IReadOnlyList<PricedLine> lines, string currency)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<decimal> lineNets = [.. lines.Select(line => new Money(line.Quantity.Value * line.UnitPrice, currency).Round().Amount)];

        List<TaxSubtotal> subtotals = [.. lines
            .Select((line, index) => (line, net: lineNets[index]))
            .GroupBy(pair => (pair.line.Category, pair.line.VatRate))
            .OrderBy(group => group.Key.Category).ThenByDescending(group => group.Key.VatRate.Value)
            .Select(group =>
            {
                decimal taxable = group.Sum(pair => pair.net);
                decimal tax = group.Key.VatRate.Of(new Money(taxable, currency)).Amount;
                return new TaxSubtotal(group.Key.Category, group.Key.VatRate.Value, taxable, tax);
            })];

        decimal net = lineNets.Sum();
        decimal vat = subtotals.Sum(subtotal => subtotal.TaxAmount);
        return new InvoiceTotals(net, vat, net + vat, currency, lineNets, subtotals);
    }
}
