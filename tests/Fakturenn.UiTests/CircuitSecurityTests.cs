using AwesomeAssertions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Fakturenn.UiTests;

/// <summary>
/// What a live circuit must not get away with. A circuit makes no HTTP request after it
/// starts, so neither the cookie's security-stamp check nor <c>IHttpContextAccessor</c>
/// sees what an interactive page does (spec section 8).
/// </summary>
[Collection(nameof(SharedIdentityHost))]
public sealed class CircuitSecurityTests(AuthenticatedWebAppFixture app) : IAsyncLifetime
{
    // private Fields
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    // public Methods
    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();

        // CreateEnrolledUserAsync writes a user row, which closes /setup for good; the
        // first-run journey has to have happened before it (see IMPLEMENTATION-NOTES).
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
    public async Task A_user_locked_while_a_business_page_is_open_is_refused_on_the_next_change()
    {
        // Review S3. The page is open and its circuit is live; the lock rotates the
        // security stamp; the revalidating provider notices within one interval; the next
        // operation is refused rather than saved.
        UiAccount account = await app.CreateEnrolledUserAsync("circuit-lock@example.test", "Korrekt-Pferd-42");
        await app.AssignAdministratorRoleAsync(account.Email);
        IPage page = await app.SignInAsync(_browser!, account);
        await page.GotoAsync(app.Url("/catalog"));
        await Expect(page.GetByTestId("catalog-save")).ToBeVisibleAsync();

        await app.LockUserAsync(account.Email);
        // SecurityStampValidatorOptions.ValidationInterval is one minute, and the provider
        // revalidates on that interval from the circuit's start.
        await Task.Delay(TimeSpan.FromSeconds(65), TestContext.Current.CancellationToken);

        await page.GetByTestId("catalog-number").FillAsync("LOCKED-OUT");
        await page.GetByTestId("catalog-name").FillAsync("Never saved");
        await page.GetByTestId("catalog-save").ClickAsync();

        await AuthenticatedWebAppFixture.ArriveAtAsync(page, "/account/denied", "denied-message");
        (await app.ReadCatalogItemCreatedByAsync("LOCKED-OUT")).Should().BeNull("a refused operation saves nothing");
    }

    [Fact]
    public async Task A_row_written_through_an_interactive_page_carries_the_signed_in_user()
    {
        // Review C5: inside a circuit IHttpContextAccessor is stale or null, which would
        // record "system".
        IPage page = await app.SignInAsAdministratorAsync(_browser!);
        await page.GotoAsync(app.Url("/catalog"));
        await page.GetByTestId("catalog-number").FillAsync("PROVENANCE-1");
        await page.GetByTestId("catalog-name").FillAsync("Provenance probe");
        await page.GetByTestId("catalog-price").FillAsync("1");
        await page.GetByTestId("catalog-save").ClickAsync();
        await Expect(page.GetByTestId("catalog-row").Filter(new() { HasText = "PROVENANCE-1" })).ToBeVisibleAsync();

        (await app.ReadCatalogItemCreatedByAsync("PROVENANCE-1")).Should().Be(app.AdminEmail);
    }
}
