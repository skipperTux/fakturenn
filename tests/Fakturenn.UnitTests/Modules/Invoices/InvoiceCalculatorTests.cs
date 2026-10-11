using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class InvoiceCalculatorTests
{
    // public Methods
    [Fact]
    public void The_walking_skeleton_totals()
    {
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(8m, 100.00m, 19m, TaxCategory.S)], "EUR");

        totals.Net.Should().Be(800.00m);
        totals.Vat.Should().Be(152.00m);
        totals.Gross.Should().Be(952.00m);
    }

    [Fact]
    public void A_rounding_midpoint_rounds_away_from_zero()
    {
        // Review M4: 1307.50 x 19% = 248.425. Commercial rounding gives 248.43;
        // Math.Round's default, to-even, would give 248.42.
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(12.5m, 104.60m, 19m, TaxCategory.S)], "EUR");

        totals.Net.Should().Be(1307.50m);
        totals.Vat.Should().Be(248.43m);
    }

    [Fact]
    public void Vat_is_rounded_once_per_category_not_per_line()
    {
        // Review M13: two lines of 2.50 at 19%. Per line, 0.475 rounds to 0.48 twice, 0.96.
        // Per category, 5.00 x 19% = 0.95. EN 16931 computes per category.
        InvoiceTotals totals = InvoiceCalculator.Calculate(
            [new PricedLine(1m, 2.50m, 19m, TaxCategory.S), new PricedLine(1m, 2.50m, 19m, TaxCategory.S)], "EUR");

        totals.Vat.Should().Be(0.95m);
        totals.Subtotals.Should().ContainSingle().Which.Should().Be(new TaxSubtotal(TaxCategory.S, 19m, 5.00m, 0.95m));
    }

    [Fact]
    public void A_fractional_quantity_nets_to_two_decimals()
    {
        InvoiceTotals totals = InvoiceCalculator.Calculate([new PricedLine(0.333m, 100.00m, 19m, TaxCategory.S)], "EUR");

        totals.LineNets.Should().Equal(33.30m);
        totals.Net.Should().Be(33.30m);
    }

    [Fact]
    public void Two_rates_produce_two_subtotals()
    {
        InvoiceTotals totals = InvoiceCalculator.Calculate(
            [new PricedLine(1m, 100m, 19m, TaxCategory.S), new PricedLine(1m, 100m, 7m, TaxCategory.S)], "EUR");

        totals.Subtotals.Should().HaveCount(2);
        totals.Vat.Should().Be(26.00m);
    }
}
