using System.Globalization;
using System.Text.RegularExpressions;

namespace Fakturenn.SharedKernel;

/// <summary>
/// Format checks shared by the seller and the buyer: an ISO 3166-1 alpha-2 country code and
/// an EU VAT ID. Format and country prefix only; VIES validation is optional and lives in the
/// backlog. Patterns end in <c>\z</c>, because .NET's <c>$</c> matches before a final newline.
/// </summary>
public static partial class EuIdentifiers
{
    // public const Fields
    /// <summary>A two-letter prefix and at most twelve characters, as the pattern allows.</summary>
    public const int MaxVatIdLength = 14;

    // public static Methods
    public static bool IsCountry(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (!CountryPattern().IsMatch(code))
        {
            return false;
        }

        try
        {
            return string.Equals(new RegionInfo(code).TwoLetterISORegionName, code, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsVatIdFor(string country, string vatId)
    {
        ArgumentNullException.ThrowIfNull(vatId);

        Match match = VatIdPattern().Match(vatId);
        if (!match.Success)
        {
            return false;
        }

        // Greece uses EL in VAT IDs and GR as its ISO 3166 code.
        string prefix = match.Groups["country"].Value;
        return string.Equals(prefix, country, StringComparison.Ordinal)
            || (prefix == "EL" && country == "GR");
    }

    // private static Methods
    [GeneratedRegex("^[A-Z]{2}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();

    [GeneratedRegex("^(?<country>[A-Z]{2})[A-Z0-9+*]{2,12}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VatIdPattern();
}
