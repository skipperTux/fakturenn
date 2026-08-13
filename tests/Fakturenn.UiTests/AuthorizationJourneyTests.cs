using AwesomeAssertions;
using Microsoft.Playwright;

namespace Fakturenn.UiTests;

/// <summary>
/// What a session is allowed to reach, and for how long. These are not extra coverage:
/// each one catches a defect that shipped in an earlier draft of this plan and that every
/// other test passed over.
/// </summary>
[Collection(nameof(SharedIdentityHost))]
public sealed class AuthorizationJourneyTests(AuthenticatedWebAppFixture app) : IAsyncLifetime
{
    // private Fields

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    // public Methods

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    [Fact]
    public async Task An_administrator_reaches_an_authorized_page()
    {
        // The defect this catches: PermissionAuthorizationHandler reads
        // fakturenn.permission claims, and for one draft of this plan NOTHING wrote
        // them. Every [Authorize(Policy = ...)] would have returned 403, including
        // the administrator's own page. The unit tests passed throughout, because
        // they construct a principal with the claims already present -- they assert
        // the handler's inputs, not its effect.
        IPage page = await app.SignInAsAdministratorAsync(_browser!);

        IResponse? response = await page.GotoAsync(app.Url("/admin/users"));

        response!.Status.Should().Be(200, "the administrator holds users.read");
        new Uri(response.Url).AbsolutePath.Should().Be("/admin/users");
        await page.GetByTestId("user-table").WaitForAsync();
    }

    [Fact]
    public async Task A_signed_in_user_without_the_permission_is_turned_away()
    {
        // The other half of the same claim, and the half the plan left out. A page that
        // let everybody in would pass the test above unchanged; only a caller who must be
        // refused can tell an enforced policy from a decorative one.
        //
        // The expected outcome is a redirect to /account/denied, NOT a 403:
        // ConfigureApplicationCookie sets AccessDeniedPath, so the authorization
        // middleware's forbid is turned into a redirect. Asserting on the status code
        // would assert the framework's default rather than this application's behaviour.
        UiAccount account = await app.CreateEnrolledUserAsync(
            "no-permissions@example.test", "Str0ng!Passw0rd!");

        IPage page = await app.SignInAsync(_browser!, account);

        IResponse? response = await page.GotoAsync(app.Url("/admin/users"));

        new Uri(response!.Url).AbsolutePath.Should().Be(
            "/account/denied", "a signed-in caller without users.read must not reach the page");
        await page.GetByTestId("denied-message").WaitForAsync();
    }

    [Fact]
    public async Task Locking_a_user_stops_their_existing_session()
    {
        // The defect this catches: Identity rotates the security stamp on password
        // and two-factor changes but NOT on lockout, and the default validation
        // interval is thirty minutes. Without explicit rotation plus a short
        // interval, "lock" is a database column that does nothing to anyone already
        // signed in -- which is not lock.
        //
        // The victim is a user of its own rather than the administrator: the
        // administrator's session is cached and replayed by every other test in this
        // collection, and locking it would end those too.
        UiAccount account = await app.CreateEnrolledUserAsync(
            "lock-victim@example.test", "Str0ng!Passw0rd!");

        IPage victim = await app.SignInAsync(_browser!, account);
        IResponse? beforeLock = await victim.GotoAsync(app.Url("/account/recovery-codes"));
        beforeLock!.Url.Should().NotContain("/account/login", "the victim must start with a working session");

        await app.LockUserAsync(account.Email);

        // The stamp validation interval is one minute; poll rather than sleep a flat
        // minute, so the test is fast when it works and still fails when it does not.
        bool signedOut = false;
        for (int attempt = 0; attempt < 40 && !signedOut; attempt++)
        {
            await Task.Delay(2000, TestContext.Current.CancellationToken);
            IResponse? response = await victim.GotoAsync(app.Url("/account/recovery-codes"));
            signedOut = response!.Url.Contains("/account/login", StringComparison.Ordinal);
        }

        signedOut.Should().BeTrue(
            "a locked user's existing cookie must stop working within the stamp validation interval");
    }
}
