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

    [Fact]
    public void Text_alongside_child_elements_is_not_ignored()
    {
        const string expected = "<Note>Please pay by <Date>2026-09-01</Date> thanks</Note>";
        const string actual = "<Note>Kindly pay by <Date>2026-09-01</Date> thanks</Note>";

        NormalizingXmlComparer.Compare(expected, actual).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Documents_differing_only_by_namespace_are_reported_as_different()
    {
        // CII and UBL differ precisely here. A comparer that ignored the namespace
        // would report a UBL invoice as matching its CII golden file.
        const string cii = "<Invoice xmlns=\"urn:cen.eu:en16931:2017:cii\"><Total>952.00</Total></Invoice>";
        const string ubl = "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"><Total>952.00</Total></Invoice>";

        NormalizingXmlComparer.Compare(cii, ubl).IsMatch.Should().BeFalse();
    }
}
