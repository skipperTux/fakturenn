using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class TaxDecisionTests
{
    // public Methods
    [Fact]
    public void A_domestic_invoice_at_a_positive_rate_is_standard_rated()
    {
        TaxDecision.Decide("DE", "DE", 19m, "EUR", "EUR").Should().BeNull();
    }

    [Theory]
    [InlineData("AT")]
    [InlineData("FR")]
    [InlineData("CH")]
    [InlineData("US")]
    public void Every_cross_border_customer_is_refused(string customerCountry)
    {
        // Spec section 7: silently charging 19% to an EU business with a VAT ID is a legal
        // problem. Reverse charge, OSS and export arrive in M2.
        TaxDecision.Decide("DE", customerCountry, 19m, "EUR", "EUR")
            .Should().Be(FinalizationRefusal.CrossBorderVatNotImplemented);
    }

    [Fact]
    public void A_standard_rate_of_zero_is_refused()
    {
        // EN 16931 BR-S-05; review C6.
        TaxDecision.Decide("DE", "DE", 0m, "EUR", "EUR").Should().Be(FinalizationRefusal.StandardRateMustBePositive);
    }

    [Fact]
    public void An_item_in_another_currency_is_refused()
    {
        TaxDecision.Decide("DE", "DE", 19m, "EUR", "USD").Should().Be(FinalizationRefusal.CurrencyMismatch);
    }
}
