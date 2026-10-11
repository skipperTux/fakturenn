using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class InvoiceNumberFormatTests
{
    // private static readonly Fields
    private static readonly DateOnly _lastOfAugust = new(2026, 8, 31);

    // public Methods
    [Theory]
    [InlineData(ResetScope.PerDay, "R", 1, 8, "R2608318")]
    [InlineData(ResetScope.PerMonth, "R", 3, 12, "R2608012")]
    [InlineData(ResetScope.PerYear, "", 4, 7, "20260007")]
    [InlineData(ResetScope.Continuous, "INV", 5, 451, "INV00451")]
    public void A_number_is_prefix_date_part_and_padded_counter(ResetScope scope, string prefix, int padding, long counter, string expected)
    {
        InvoiceNumberFormat.Format(new NumberScheme(prefix, scope, padding, 1), _lastOfAugust, counter).Should().Be(expected);
    }

    [Fact]
    public void A_counter_wider_than_the_padding_is_not_truncated()
    {
        InvoiceNumberFormat.Format(new NumberScheme("R", ResetScope.PerDay, 1, 1), _lastOfAugust, 12).Should().Be("R26083112");
    }

    [Theory]
    [InlineData(ResetScope.PerDay, "260831")]
    [InlineData(ResetScope.PerMonth, "2608")]
    [InlineData(ResetScope.PerYear, "2026")]
    [InlineData(ResetScope.Continuous, "continuous")]
    public void The_scope_key_comes_from_the_issue_date(ResetScope scope, string expected)
    {
        InvoiceNumberFormat.ScopeKey(scope, _lastOfAugust).Should().Be(expected);
    }
}
