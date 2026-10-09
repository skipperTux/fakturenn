using AwesomeAssertions;
using Fakturenn.Modules.Organizations;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.UnitTests.Modules;

public sealed class OrganizationRulesTests
{
    // public Methods
    [Fact]
    public void The_walking_skeleton_organization_is_valid()
    {
        OrganizationRules.Validate(Valid()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("R", true)]
    [InlineData("RE2026", true)]
    [InlineData("ABCDEFGHI", true)]
    [InlineData("ABCDEFGHIJ", false)]
    [InlineData("RÄ", false)]
    [InlineData("R E", false)]
    [InlineData("R-", false)]
    public void Prefix_rules(string prefix, bool valid)
    {
        IReadOnlyList<OrganizationError> errors = OrganizationRules.Validate(Valid() with { NumberPrefix = prefix });

        if (valid)
        {
            errors.Should().BeEmpty();
        }
        else
        {
            errors.Should().Contain(OrganizationError.PrefixInvalid);
        }
    }

    [Theory]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("DE89 3704 0044 0532 0130 00", true)]
    [InlineData("DE00000000000000000000", false)]
    [InlineData("DE89370400440532013001", false)]
    [InlineData("XX", false)]
    public void Iban_passes_mod_97(string iban, bool valid)
    {
        IReadOnlyList<OrganizationError> errors = OrganizationRules.Validate(Valid() with { Iban = iban });

        (!errors.Contains(OrganizationError.IbanInvalid)).Should().Be(valid);
    }

    [Theory]
    [InlineData("DE", "DE123456789", true)]
    [InlineData("DE", "AT123456789", false)]
    [InlineData("GR", "EL123456789", true)]
    [InlineData("DE", "DE12", true)]
    [InlineData("DE", "DE1", false)]
    public void Vat_id_is_checked_for_format_only(string country, string vatId, bool valid)
    {
        IReadOnlyList<OrganizationError> errors =
            OrganizationRules.Validate(Valid() with { CountryCode = country, VatId = vatId });

        (!errors.Contains(OrganizationError.VatIdInvalid)).Should().Be(valid);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("DEU")]
    [InlineData("ZZ")]
    public void Country_is_an_iso_3166_alpha_2_code(string country)
    {
        OrganizationRules.Validate(Valid() with { CountryCode = country, VatId = "" })
            .Should().Contain(OrganizationError.CountryInvalid);
    }

    [Fact]
    public void A_trailing_newline_is_not_part_of_a_valid_value()
    {
        // .NET's $ matches before a final \n; the patterns anchor with \z instead.
        OrganizationRules.Validate(Valid() with { NumberPrefix = "R\n" }).Should().Contain(OrganizationError.PrefixInvalid);
        OrganizationRules.Validate(Valid() with { VatId = "DE123456789\n" }).Should().Contain(OrganizationError.VatIdInvalid);
        OrganizationRules.Validate(Valid() with { Iban = "DE89370400440532013000\n" }).Should().Contain(OrganizationError.IbanInvalid);
        OrganizationRules.Validate(Valid() with { Currency = "EUR\n" }).Should().Contain(OrganizationError.CurrencyInvalid);
        OrganizationRules.Validate(Valid() with { CountryCode = "DE\n", VatId = "" }).Should().Contain(OrganizationError.CountryInvalid);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Legal_name_fits_its_column(int length, bool valid)
    {
        IReadOnlyList<OrganizationError> errors =
            OrganizationRules.Validate(Valid() with { LegalName = new string('x', length) });

        (!errors.Contains(OrganizationError.LegalNameTooLong)).Should().Be(valid);
    }

    [Fact]
    public void An_unknown_time_zone_is_refused()
    {
        OrganizationRules.Validate(Valid() with { TimeZoneId = "Mars/Olympus_Mons" })
            .Should().Contain(OrganizationError.TimeZoneInvalid);
    }

    [Fact]
    public void Empty_fields_are_allowed_while_the_organization_is_being_completed()
    {
        // The seeded row is empty, and the page saves partial input. Completeness is
        // finalization's question (spec section 5 step 3), not the form's.
        OrganizationRules.Validate(OrganizationForm.Empty).Should().BeEmpty();
    }

    // private static Methods
    private static OrganizationForm Valid() => new(
        LegalName: "Example Consulting",
        CountryCode: "DE",
        VatId: "DE123456789",
        Iban: "DE89370400440532013000",
        Currency: "EUR",
        TimeZoneId: "Europe/Berlin",
        PaymentTermsDays: 14,
        IssueDateDefault: IssueDateDefault.Today,
        NumberPrefix: "R",
        NumberScope: ResetScope.PerDay,
        NumberPadding: 1,
        NumberNextValue: 1);
}
