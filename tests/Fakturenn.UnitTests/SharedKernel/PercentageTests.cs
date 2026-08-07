using AwesomeAssertions;
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.SharedKernel;

public sealed class PercentageTests
{
    [Fact]
    public void Nineteen_percent_of_the_walking_skeleton_net_amount_is_the_documented_tax()
    {
        // docs/planning/WALKING-SKELETON.md: net 800.00, VAT 19%, VAT 152.00.
        var tax = new Percentage(19m).Of(new Money(800.00m, "EUR"));

        tax.Should().Be(new Money(152.00m, "EUR"));
    }

    [Fact]
    public void The_result_keeps_the_currency_of_the_base_amount()
    {
        new Percentage(19m).Of(new Money(100m, "CHF")).Currency.Should().Be("CHF");
    }

    [Fact]
    public void The_result_is_rounded_to_two_decimal_places()
    {
        new Percentage(19m).Of(new Money(0.99m, "EUR")).Amount.Should().Be(0.19m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void A_percentage_outside_zero_to_one_hundred_is_rejected(decimal value)
    {
        var create = () => new Percentage(value);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
