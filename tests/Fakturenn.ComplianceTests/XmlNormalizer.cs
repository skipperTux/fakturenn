using System.Xml.Linq;

namespace Fakturenn.ComplianceTests;

/// <summary>
/// Removes differences that carry no semantics: comments, insignificant
/// whitespace, and attribute order. Element order is preserved, because
/// EN 16931 syntax bindings define ordered sequences; namespace URIs are
/// preserved too, because the element's <see cref="XName"/> carries them,
/// and CII versus UBL invoices differ precisely there. An element's own text
/// is preserved and collapsed even when it also has child elements (mixed
/// content), so text is compared rather than silently discarded.
/// </summary>
public static class XmlNormalizer
{
    public static XElement Normalize(XElement element)
    {
        var normalized = new XElement(element.Name);

        foreach (XAttribute attribute in element.Attributes()
                     .Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            normalized.SetAttributeValue(attribute.Name, attribute.Value.Trim());
        }

        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    normalized.Add(Normalize(child));
                    break;
                case XText text:
                    string collapsed = CollapseWhitespace(text.Value);
                    if (collapsed.Length > 0)
                    {
                        normalized.Add(new XText(collapsed));
                    }

                    break;
                default:
                    // Comments, processing instructions and the like carry no semantics.
                    break;
            }
        }

        return normalized;
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
