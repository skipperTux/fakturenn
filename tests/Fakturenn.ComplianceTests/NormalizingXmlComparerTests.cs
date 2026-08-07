using AwesomeAssertions;

namespace Fakturenn.ComplianceTests;

public sealed class NormalizingXmlComparerTests
{
    [Fact]
    public void Identical_documents_match()
    {
        const string xml = "<Invoice><Total currency=\"EUR\">952.00</Total></Invoice>";

        NormalizingXmlComparer.Compare(xml, xml).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Insignificant_whitespace_and_indentation_are_ignored()
    {
        const string expected = "<Invoice><Total currency=\"EUR\">952.00</Total></Invoice>";
        const string actual = """
            <Invoice>
                <Total currency="EUR">952.00</Total>
            </Invoice>
            """;

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Attribute_order_is_ignored()
    {
        const string expected = "<Total currency=\"EUR\" scheme=\"EN16931\">952.00</Total>";
        const string actual = "<Total scheme=\"EN16931\" currency=\"EUR\">952.00</Total>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Comments_are_ignored()
    {
        const string expected = "<Invoice><Total>952.00</Total></Invoice>";
        const string actual = "<Invoice><!-- generated --><Total>952.00</Total></Invoice>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void A_different_value_is_reported_as_a_difference()
    {
        const string expected = "<Invoice><Total>952.00</Total></Invoice>";
        const string actual = "<Invoice><Total>952.01</Total></Invoice>";

        XmlComparison comparison = NormalizingXmlComparer.Compare(expected, actual);

        comparison.IsMatch.Should().BeFalse();
        comparison.Differences.Should().ContainSingle()
            .Which.Should().Contain("952.00").And.Contain("952.01");
    }

    [Fact]
    public void A_missing_element_is_reported_as_a_difference()
    {
        const string expected = "<Invoice><Total>952.00</Total><BuyerReference>C-4711</BuyerReference></Invoice>";
        const string actual = "<Invoice><Total>952.00</Total></Invoice>";

        XmlComparison comparison = NormalizingXmlComparer.Compare(expected, actual);

        comparison.IsMatch.Should().BeFalse();
        comparison.Differences.Should().NotBeEmpty();
    }

    [Fact]
    public void A_different_attribute_value_is_reported_as_a_difference()
    {
        const string expected = "<Total currency=\"EUR\">952.00</Total>";
        const string actual = "<Total currency=\"CHF\">952.00</Total>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Element_order_is_significant_because_EN_16931_sequences_are_ordered()
    {
        const string expected = "<Invoice><A>1</A><B>2</B></Invoice>";
        const string actual = "<Invoice><B>2</B><A>1</A></Invoice>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeFalse();
    }
}
