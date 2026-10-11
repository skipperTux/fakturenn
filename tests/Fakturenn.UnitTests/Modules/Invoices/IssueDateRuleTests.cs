using AwesomeAssertions;
using Fakturenn.Modules.Invoices.Domain;
using Fakturenn.Modules.Organizations.Contracts;

namespace Fakturenn.UnitTests.Modules.Invoices;

public sealed class IssueDateRuleTests
{
    // public Methods
    [Fact]
    public void The_default_issue_date_is_today_in_the_organization_time_zone()
    {
        // Review Focus 1: 23:30 UTC on 31 August is already 1 September in Berlin.
        DateTimeOffset utcNow = new(2026, 8, 31, 23, 30, 0, TimeSpan.Zero);

        IssueDateRule.DefaultFor(IssueDateDefault.Today, utcNow, "Europe/Berlin").Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void Billing_the_previous_month_defaults_to_its_last_day()
    {
        DateTimeOffset utcNow = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

        IssueDateRule.DefaultFor(IssueDateDefault.LastDayOfPreviousMonth, utcNow, "Europe/Berlin")
            .Should().Be(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void Billing_the_previous_month_in_march_of_a_leap_year()
    {
        DateTimeOffset utcNow = new(2028, 3, 1, 12, 0, 0, TimeSpan.Zero);

        IssueDateRule.DefaultFor(IssueDateDefault.LastDayOfPreviousMonth, utcNow, "Europe/Berlin")
            .Should().Be(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void The_default_service_period_is_the_issue_date_month()
    {
        IssueDateRule.ServicePeriodFor(new DateOnly(2026, 8, 31))
            .Should().Be((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
    }
}
