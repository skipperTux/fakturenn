using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.Playwright;

namespace Fakturenn.UiTests;

/// <summary>
/// Proves that a German browser is actually served German, on the pages this epic adds.
/// <para>
/// <c>SharedResourceTests</c> proves the two resource files agree and that every key the
/// code asks for exists in both. Neither of those proves a single German word reaches a
/// screen: the resources could be perfect while <c>UseRequestLocalization</c> sits in the
/// wrong place in the pipeline, or the satellite assembly fails to ship, or a page holds a
/// literal that was never extracted. Only a real browser with a real
/// <c>Accept-Language</c> header settles it.
/// </para>
/// <para>
/// The English side is covered where it already was: every other test in this suite selects
/// on <c>data-testid</c>, which is language-independent by construction, and
/// <c>HomePageTests</c> asserts both taglines. Nothing here needs an English counterpart
/// beyond the <c>lang</c> attribute, which is new.
/// </para>
/// <para>
/// There is deliberately <b>no language picker</b> to test. The culture comes from
/// <c>Accept-Language</c>, which is the mechanism the application already chose; adding a
/// picker is a product decision nobody has taken.
/// </para>
/// </summary>
[Collection(nameof(SharedIdentityHost))]
public sealed class LocalizationJourneyTests(AuthenticatedWebAppFixture app) : IAsyncLifetime
{
    // private Fields

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    // public Methods

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();

        // /account/login redirects to /setup while no user exists, so even the
        // unauthenticated half of this class needs the first-run journey to have happened.
        // Idempotent, and shared with the rest of the collection.
        await app.EnsureAdministratorAsync(_browser);
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
    public async Task The_sign_in_page_renders_in_german_for_a_german_browser()
    {
        IBrowserContext context = await AuthenticatedWebAppFixture.NewContextAsync(
            _browser!, storageState: null, AuthenticatedWebAppFixture.GermanLocale);
        IPage page = await context.NewPageAsync();

        await page.GotoAsync(app.Url("/account/login"));

        using AssertionScope scope = new();

        // The heading, the field label and the button: markup content, a component
        // parameter and a nested child render fragment respectively, which are three
        // different ways a literal can hide in a Razor page.
        (await page.GetByTestId("login-title").TextContentAsync()).Should().Be("Anmelden");
        (await page.GetByTestId("login-submit").TextContentAsync())!.Trim().Should().Be("Anmelden");
        (await page.GetByLabel("E-Mail").CountAsync()).Should()
            .Be(1, "the e-mail field's label must be the German one");

        // Correction (c). Serving German under lang="en" makes a screen reader read it with
        // English phonetics and makes the browser offer to translate it into the language
        // it is already in.
        (await page.GetAttributeAsync("html", "lang")).Should().Be("de");

        await context.CloseAsync();
    }

    [Fact]
    public async Task An_authenticated_page_renders_in_german_for_a_german_browser()
    {
        // The administration list, reached with the cookie jar the English first-run
        // journey produced. The cookie carries no language; the header does.
        IPage page = await app.SignInAsAdministratorAsync(
            _browser!, AuthenticatedWebAppFixture.GermanLocale);

        await page.GotoAsync(app.Url("/admin/users"));

        using AssertionScope scope = new();

        (await page.GetByTestId("admin-users-title").TextContentAsync()).Should().Be("Benutzer");
        (await page.GetByTestId("create-user-submit").TextContentAsync())!.Trim().Should()
            .Be("Benutzer anlegen");

        // A table header and a rendered cell value, neither of which is a form control --
        // the row body is where "enrolled"/"pending" lived as bare literals.
        string table = (await page.GetByTestId("user-table").TextContentAsync())!;
        table.Should().Contain("Gesperrt bis").And.Contain("eingerichtet");

        (await page.GetAttributeAsync("html", "lang")).Should().Be("de");
    }

    [Fact]
    public async Task An_english_browser_still_gets_english_and_lang_en()
    {
        // The other edge of correction (c): making the attribute dynamic must not leave it
        // empty or set to the host machine's culture for a default request.
        IPage page = await app.SignInAsAdministratorAsync(_browser!);

        await page.GotoAsync(app.Url("/admin/users"));

        using AssertionScope scope = new();

        (await page.GetByTestId("admin-users-title").TextContentAsync()).Should().Be("Users");
        (await page.GetAttributeAsync("html", "lang")).Should().Be("en");
    }
}
