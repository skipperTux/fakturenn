using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;

namespace Fakturenn.Modules.Identity.UnitTests;

public sealed class EnrolmentGatePathTests
{
    [Theory]
    [InlineData("/account/enrol-totp")]
    [InlineData("/account/enrol-totp/verify")]
    [InlineData("/account/recovery-codes")]
    [InlineData("/account/change-password")]
    [InlineData("/account/change-password/submit")]
    [InlineData("/account/logout")]
    [InlineData("/alive")]
    [InlineData("/health")]
    public void Paths_a_user_with_an_outstanding_obligation_still_needs_are_allowed(string path)
    {
        EnrolmentGate.IsAllowedWhilePendingObligations(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/admin/users")]
    [InlineData("/invoices")]
    [InlineData("/account/login")]
    [InlineData("/_blazor")]
    [InlineData("/_content/MudBlazor/MudBlazor.min.css")]
    public void Everything_else_is_blocked(string path)
    {
        EnrolmentGate.IsAllowedWhilePendingObligations(path).Should().BeFalse();
    }

    [Fact]
    public void Matching_is_case_insensitive_because_urls_are()
    {
        EnrolmentGate.IsAllowedWhilePendingObligations("/Account/Enrol-Totp").Should().BeTrue();
    }
}
