using System.Xml.Linq;

namespace Fakturenn.ComplianceTests;

public static class NormalizingXmlComparer
{
    public static XmlComparison Compare(string expectedXml, string actualXml)
    {
        XElement expected = XmlNormalizer.Normalize(XElement.Parse(expectedXml, LoadOptions.None));
        XElement actual = XmlNormalizer.Normalize(XElement.Parse(actualXml, LoadOptions.None));

        List<string> differences = [];
        CompareElements(expected, actual, expected.Name.LocalName, differences);

        return new XmlComparison(differences.Count == 0, differences);
    }

    private static void CompareElements(XElement expected, XElement actual, string path, List<string> differences)
    {
        if (expected.Name != actual.Name)
        {
            differences.Add($"{path}: expected element '{expected.Name}' but found '{actual.Name}'");
            return;
        }

        CompareAttributes(expected, actual, path, differences);

        string expectedText = OwnText(expected);
        string actualText = OwnText(actual);

        if (expectedText != actualText)
        {
            differences.Add($"{path}: expected value '{expectedText}' but found '{actualText}'");
        }

        XElement[] expectedChildren = [.. expected.Elements()];
        XElement[] actualChildren = [.. actual.Elements()];

        if (expectedChildren.Length != actualChildren.Length)
        {
            differences.Add(
                $"{path}: expected {expectedChildren.Length} child element(s) but found {actualChildren.Length}");
        }

        int shared = Math.Min(expectedChildren.Length, actualChildren.Length);
        for (int index = 0; index < shared; index++)
        {
            CompareElements(
                expectedChildren[index],
                actualChildren[index],
                $"{path}/{expectedChildren[index].Name.LocalName}[{index}]",
                differences);
        }
    }

    private static void CompareAttributes(XElement expected, XElement actual, string path, List<string> differences)
    {
        foreach (XAttribute attribute in expected.Attributes())
        {
            string? actualValue = actual.Attribute(attribute.Name)?.Value;

            if (actualValue is null)
            {
                differences.Add($"{path}: missing attribute '{attribute.Name}'");
            }
            else if (actualValue != attribute.Value)
            {
                differences.Add(
                    $"{path}@{attribute.Name}: expected '{attribute.Value}' but found '{actualValue}'");
            }
        }

        foreach (XAttribute attribute in actual.Attributes()
                     .Where(attribute => expected.Attribute(attribute.Name) is null))
        {
            differences.Add($"{path}: unexpected attribute '{attribute.Name}'");
        }
    }

    /// <summary>
    /// The element's own text, excluding text owned by descendant elements. Unlike
    /// <see cref="XElement.Value"/>, which concatenates all descendant text, this
    /// only looks at the element's direct <see cref="XText"/> children — the text
    /// interleaved with its child elements (mixed content).
    /// </summary>
    private static string OwnText(XElement element) =>
        string.Concat(element.Nodes().OfType<XText>().Select(text => text.Value));
}
