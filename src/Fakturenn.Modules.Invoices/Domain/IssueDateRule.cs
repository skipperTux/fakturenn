using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.Modules.Invoices.Domain;

/// <summary>
/// The issue date a draft starts with, taken once when the draft is created and stored on
/// it (spec section 6, review M1). "Today" is the organization's today, not the server's.
/// </summary>
public static class IssueDateRule
{
    // public static Methods
    public static DateOnly DefaultFor(IssueDateDefault rule, DateTimeOffset utcNow, string timeZoneId)
    {
        DateTime local = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime;
        DateOnly today = DateOnly.FromDateTime(local);

        return rule switch
        {
            IssueDateDefault.Today => today,
            IssueDateDefault.LastDayOfPreviousMonth => new DateOnly(today.Year, today.Month, 1).AddDays(-1),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
        };
    }

    public static (DateOnly Start, DateOnly End) ServicePeriodFor(DateOnly issueDate)
    {
        DateOnly start = new(issueDate.Year, issueDate.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}
