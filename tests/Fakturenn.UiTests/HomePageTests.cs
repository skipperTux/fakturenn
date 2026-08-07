using AwesomeAssertions;
using Microsoft.Playwright;

namespace Fakturenn.UiTests;

public sealed class HomePageTests(WebAppFixture app) : IClassFixture<WebAppFixture>, IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

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
    public async Task The_home_page_renders_the_english_tagline_by_default()
    {
        IPage page = await NewPageAsync("en-GB");

        await page.GotoAsync(app.BaseAddress);

        string? tagline = await page.GetByTestId("app-tagline").TextContentAsync();
        tagline.Should().Be("Your invoices, your identity, your infrastructure.");
    }

    [Fact]
    public async Task A_german_browser_gets_the_german_tagline()
    {
        // Proves resources, the localization middleware and the Accept-Language
        // provider are wired together, not merely present.
        IPage page = await NewPageAsync("de-DE");

        await page.GotoAsync(app.BaseAddress);

        string? tagline = await page.GetByTestId("app-tagline").TextContentAsync();
        tagline.Should().Be("Ihre Rechnungen, Ihre Identität, Ihre Infrastruktur.");
    }

    [Fact]
    public async Task The_liveness_endpoint_reports_healthy_without_a_database()
    {
        IPage page = await NewPageAsync("en-GB");

        IResponse? response = await page.GotoAsync($"{app.BaseAddress}/alive");

        response!.Status.Should().Be(200);
        (await response.TextAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task The_readiness_endpoint_reports_unhealthy_without_a_database()
    {
        // Readiness must report, never throw. This returned 500 for a whole task
        // because nothing asserted the status code.
        IPage page = await NewPageAsync("en-GB");

        IResponse? response = await page.GotoAsync($"{app.BaseAddress}/health");

        response!.Status.Should().Be(503);
    }

    private async Task<IPage> NewPageAsync(string locale)
    {
        IBrowserContext context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = locale,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Accept-Language"] = locale },
        });

        return await context.NewPageAsync();
    }
}
