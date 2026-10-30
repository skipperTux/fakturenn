using AwesomeAssertions;
using Fakturenn.Modules.Catalog;

namespace Fakturenn.UnitTests.Modules;

public sealed class CatalogRulesTests
{
    // public Methods
    [Fact]
    public void The_walking_skeleton_item_is_valid()
    {
        CatalogRules.Validate(Item()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-0.01, CatalogError.PriceNegative)]
    public void Price_is_not_negative(decimal price, CatalogError expected)
    {
        CatalogRules.Validate(Item() with { UnitPrice = price }).Should().Contain(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(100.5)]
    [InlineData(19.555)]
    public void Vat_rate_is_a_percentage_with_at_most_two_decimals(decimal rate)
    {
        CatalogRules.Validate(Item() with { VatRate = rate }).Should().Contain(CatalogError.VatRateInvalid);
    }

    [Theory]
    [InlineData(19.5)]
    [InlineData(100)]
    public void A_vat_rate_up_to_100_with_at_most_two_decimals_is_valid(decimal rate)
    {
        CatalogRules.Validate(Item() with { VatRate = rate }).Should().BeEmpty();
    }

    [Theory]
    [InlineData("hur")]
    [InlineData("")]
    [InlineData("HOURS")]
    public void Unit_is_an_uppercase_code(string unit)
    {
        CatalogRules.Validate(Item() with { Unit = unit }).Should().Contain(CatalogError.UnitInvalid);
    }

    [Fact]
    public void A_trailing_newline_is_not_part_of_a_valid_value()
    {
        // .NET's $ matches before a final \n; the patterns anchor with \z instead.
        CatalogRules.Validate(Item() with { Unit = "HUR\n" }).Should().Contain(CatalogError.UnitInvalid);
        CatalogRules.Validate(Item() with { Currency = "EUR\n" }).Should().Contain(CatalogError.CurrencyInvalid);
    }

    [Theory]
    [InlineData(50, false)]
    [InlineData(51, true)]
    public void Item_number_fits_its_column(int length, bool tooLong)
    {
        IReadOnlyList<CatalogError> errors = CatalogRules.Validate(Item() with { CatalogItemNumber = new string('9', length) });

        if (tooLong)
        {
            errors.Should().Contain(CatalogError.NumberTooLong);
        }
        else
        {
            errors.Should().NotContain(CatalogError.NumberTooLong);
        }
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(201, true)]
    public void Name_fits_its_column(int length, bool tooLong)
    {
        IReadOnlyList<CatalogError> errors = CatalogRules.Validate(Item() with { Name = new string('x', length) });

        if (tooLong)
        {
            errors.Should().Contain(CatalogError.NameTooLong);
        }
        else
        {
            errors.Should().NotContain(CatalogError.NameTooLong);
        }
    }

    [Theory]
    [InlineData("0.000001", false)]
    [InlineData("0.0000001", true)]
    [InlineData("104.60000", false)]
    [InlineData("99999999999999.999999", false)]
    [InlineData("100000000000000", true)]
    public void Price_fits_numeric_20_6_without_rounding(string price, bool outOfRange)
    {
        IReadOnlyList<CatalogError> errors = CatalogRules.Validate(
            Item() with { UnitPrice = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture) });

        if (outOfRange)
        {
            errors.Should().Contain(CatalogError.PriceOutOfRange);
        }
        else
        {
            errors.Should().NotContain(CatalogError.PriceOutOfRange);
        }
    }

    // private static Methods
    private static CatalogItemForm Item() =>
        new(null, "DEV-BACKEND", "Backend development", "HUR", 100.00m, "EUR", 19m);
}
