using AwesomeAssertions;
using Fakturenn.SharedKernel;

namespace Fakturenn.UnitTests.SharedKernel;

public sealed class MoneyTests
{
    [Fact]
    public void Adding_two_amounts_in_the_same_currency_sums_them()
    {
        var sum = new Money(800.00m, "EUR") + new Money(152.00m, "EUR");

        sum.Should().Be(new Money(952.00m, "EUR"));
    }

    [Fact]
    public void Adding_two_amounts_in_different_currencies_throws()
    {
        var add = () => new Money(1m, "EUR") + new Money(1m, "CHF");

        add.Should().Throw<InvalidOperationException>()
            .WithMessage("*EUR*CHF*");
    }

    [Fact]
    public void Rounding_uses_commercial_rounding_away_from_zero()
    {
        new Money(0.125m, "EUR").Round().Amount.Should().Be(0.13m);
        new Money(-0.125m, "EUR").Round().Amount.Should().Be(-0.13m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void A_currency_that_is_not_three_uppercase_letters_is_rejected(string currency)
    {
        var create = () => new Money(1m, currency);

        create.Should().Throw<ArgumentException>();
    }
}
