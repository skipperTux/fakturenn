using AwesomeAssertions;
using Microsoft.Playwright;

namespace Fakturenn.UiTests;

/// <summary>
/// The journeys SPIKE-009 exists to close: first-run setup, forced enrolment, and a
/// password-plus-TOTP sign-in driven through a real browser against the real application.
/// <para>
/// Every test here is independent of the order the others run in. The first-run journey
/// itself can happen only once per instance, so it lives in
/// <see cref="AuthenticatedWebAppFixture.EnsureAdministratorAsync"/> and any test that
/// needs an enrolled administrator asks for one.
/// </para>
/// </summary>
[Collection(nameof(SharedIdentityHost))]
public sealed class IdentityJourneyTests(AuthenticatedWebAppFixture app) : IAsyncLifetime
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
    public async Task Setup_then_password_and_totp_sign_in_reaches_the_application()
    {
        // The journey itself runs inside the fixture, asserting at every step; what this
        // test adds is that its product is a working session. A cookie jar that came out
        // of the real setup, the real password form and a real RFC 6238 code must open a
        // page that only an authenticated user reaches.
        IPage page = await app.SignInAsAdministratorAsync(_browser!);

        IResponse? response = await page.GotoAsync(app.Url("/account/recovery-codes"));

        response!.Url.Should().NotContain(
            "/account/login", "the session the journey produced must still be a session");
        await page.GetByTestId("recovery-empty").WaitForAsync();
    }

    [Fact]
    public async Task A_wrong_authenticator_code_does_not_sign_the_user_in()
    {
        // The journey above submits a CORRECT code, so it would still pass against an
        // endpoint that accepted anything -- measured, by making the verifier return true
        // unconditionally. This is the half that notices: a code the account did not
        // issue must be refused, and the caller must stay on the challenge.
        await app.EnsureAdministratorAsync(_browser!);

        IBrowserContext context = await AuthenticatedWebAppFixture.NewContextAsync(_browser!);
        IPage page = await context.NewPageAsync();

        await page.GotoAsync(app.Url("/account/login"));
        await page.GetByTestId("login-email").FillAsync(app.AdminEmail);
        await page.GetByTestId("login-password").FillAsync(app.AdminPassword);
        await page.GetByTestId("login-submit").ClickAsync();

        await AuthenticatedWebAppFixture.ArriveAtAsync(page, "/account/login-2fa", "twofa-form");
        await page.GetByTestId("twofa-code").FillAsync(AuthenticatedWebAppFixture.WrongCodeFor(app.TotpSecret));
        await page.GetByTestId("twofa-submit").ClickAsync();

        await page.GetByTestId("twofa-error").WaitForAsync();
        new Uri(page.Url).AbsolutePath.Should().Be(
            "/account/login-2fa", "a refused code must not produce a session");

        IResponse? afterwards = await page.GotoAsync(app.Url("/admin/users"));
        afterwards!.Url.Should().Contain("/account/login", "no session exists to authorise anything");
    }

    [Fact]
    public async Task A_signed_in_user_can_sign_out_from_the_layout()
    {
        // POST /account/logout existed from Task 13 with no caller in the user interface,
        // so a signed-in user could not actually leave. This walks the control the layout
        // now renders, and it is also the only test that proves the form carries an
        // antiforgery token: Round A made every /account post bind IFormCollection, which
        // makes the framework answer 400 to a token-less post. A form that renders and a
        // form that works are different things, and only a real click tells them apart.
        IPage page = await app.SignInAsAdministratorAsync(_browser!);

        await page.GotoAsync(app.Url("/"));
        await page.GetByTestId("sign-out-submit").ClickAsync();

        await AuthenticatedWebAppFixture.ArriveAtAsync(page, "/account/login", "login-form");

        // Landing on the sign-in page is not proof the cookie is gone -- the redirect is
        // issued whether or not SignOutAsync ran. An authorized page is.
        IResponse? afterwards = await page.GotoAsync(app.Url("/admin/users"));

        afterwards!.Url.Should().Contain(
            "/account/login", "signing out must end the session, not just navigate away from it");
    }

    [Fact]
    public async Task The_setup_page_is_gone_once_a_user_exists()
    {
        await app.EnsureAdministratorAsync(_browser!);

        IBrowserContext context = await AuthenticatedWebAppFixture.NewContextAsync(_browser!);
        IPage page = await context.NewPageAsync();

        IResponse? response = await page.GotoAsync(app.Url("/setup"));

        // Redirected to sign-in rather than offering to create a second administrator.
        response!.Url.Should().Contain("/account/login");
    }
}
