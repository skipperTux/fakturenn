using System.Globalization;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// Prefix, the date part the scope implies, then the zero-padded counter, with no
/// separators (spec section 6, review M2). The date part comes from the invoice's own
/// issue date, never from the wall clock.
/// </summary>
public static class InvoiceNumberFormat
{
    // public static Methods
    public static string ScopeKey(ResetScope scope, DateOnly issueDate) =>
        scope == ResetScope.Continuous ? "continuous" : DatePart(scope, issueDate);

    public static string Format(NumberScheme scheme, DateOnly issueDate, long counter)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        return scheme.Prefix
            + DatePart(scheme.Scope, issueDate)
            + counter.ToString(CultureInfo.InvariantCulture).PadLeft(scheme.Padding, '0');
    }

    // private static Methods
    private static string DatePart(ResetScope scope, DateOnly issueDate) => scope switch
    {
        ResetScope.PerDay => issueDate.ToString("yyMMdd", CultureInfo.InvariantCulture),
        ResetScope.PerMonth => issueDate.ToString("yyMM", CultureInfo.InvariantCulture),
        ResetScope.PerYear => issueDate.ToString("yyyy", CultureInfo.InvariantCulture),
        ResetScope.Continuous => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
