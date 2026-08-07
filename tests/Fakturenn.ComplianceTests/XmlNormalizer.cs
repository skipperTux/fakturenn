using System.Xml.Linq;

namespace Fakturenn.ComplianceTests;

/// <summary>
/// Removes differences that carry no semantics: comments, insignificant
/// whitespace, and attribute order. Element order is preserved, because
/// EN 16931 syntax bindings define ordered sequences.
/// </summary>
public static class XmlNormalizer
{
    public static XElement Normalize(XElement element)
    {
        var normalized = new XElement(element.Name);

        foreach (XAttribute attribute in element.Attributes()
                     .Where(attribute => !attribute.IsNamespaceDeclaration)
                     .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal))
        {
            normalized.SetAttributeValue(attribute.Name, attribute.Value.Trim());
        }

        XElement[] children = [.. element.Elements()];

        if (children.Length == 0)
        {
            normalized.Value = CollapseWhitespace(element.Value);
            return normalized;
        }

        foreach (XElement child in children)
        {
            normalized.Add(Normalize(child));
        }

        return normalized;
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
