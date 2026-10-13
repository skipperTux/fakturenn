using AwesomeAssertions;
using Fakturenn.Modules.Customers;

namespace Fakturenn.UnitTests.Modules;

public sealed class CustomerRulesTests
{
    // public Methods
    [Fact]
    public void The_walking_skeleton_customer_is_valid()
    {
        CustomerRules.Validate(new CustomerForm(null, "Example Client GmbH", "DE", "", "C-4711")).Should().BeEmpty();
    }

    [Fact]
    public void A_private_individual_has_no_vat_id()
    {
        CustomerRules.Validate(new CustomerForm(null, "Erika Mustermann", "DE", "", "C-0001")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "DE", "", "C-1", CustomerError.LegalNameMissing)]
    [InlineData("A", "", "", "C-1", CustomerError.CountryInvalid)]
    [InlineData("A", "AT", "DE123456789", "C-1", CustomerError.VatIdInvalid)]
    [InlineData("A", "DE", "", " ", CustomerError.CustomerNumberMissing)]
    public void Required_fields_and_formats(string name, string country, string vatId, string number, CustomerError expected)
    {
        CustomerRules.Validate(new CustomerForm(null, name, country, vatId, number)).Should().Contain(expected);
    }

    [Fact]
    public void A_trailing_newline_is_not_part_of_a_valid_value()
    {
        // .NET's $ matches before a final \n; the patterns anchor with \z instead.
        CustomerRules.Validate(new CustomerForm(null, "A", "DE\n", "", "C-1")).Should().Contain(CustomerError.CountryInvalid);
        CustomerRules.Validate(new CustomerForm(null, "A", "DE", "DE123456789\n", "C-1")).Should().Contain(CustomerError.VatIdInvalid);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Legal_name_fits_its_column(int length, bool valid)
    {
        IReadOnlyList<CustomerError> errors =
            CustomerRules.Validate(new CustomerForm(null, new string('x', length), "DE", "", "C-1"));

        (!errors.Contains(CustomerError.LegalNameTooLong)).Should().Be(valid);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void Customer_number_fits_its_column(int length, bool valid)
    {
        IReadOnlyList<CustomerError> errors =
            CustomerRules.Validate(new CustomerForm(null, "A", "DE", "", new string('9', length)));

        (!errors.Contains(CustomerError.CustomerNumberTooLong)).Should().Be(valid);
    }
}
