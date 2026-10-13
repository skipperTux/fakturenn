using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Fakturenn.UiTests;

/// <summary>
/// The walking skeleton through the interactive pages: organization, master data, a
/// one-line invoice, its finalization. These are the first tests to drive a live circuit,
/// so they exercise what static pages never did — MudBlazor's popovers inside the page's
/// render scope (review C9), totals arriving over the circuit, and the circuit's culture
/// (review M14).
/// <para>
/// MudBlazor puts a field's <c>data-testid</c> on its <c>input</c> itself, and a select with
/// a value renders a hidden <c>input</c> beside the visible box, both carrying it — measured
/// against 9.11.0 with the Playwright MCP. Hence <see cref="ChooseAsync"/>.
/// </para>
/// </summary>
[Collection(nameof(SharedIdentityHost))]
public sealed partial class InvoiceJourneyTests(AuthenticatedWebAppFixture app) : IAsyncLifetime
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
    public async Task An_administrator_numbers_the_walking_skeleton_invoice()
    {
        IPage page = await app.SignInAsAdministratorAsync(_browser!);
        await EnsureMasterDataAsync(page, app);

        await DraftAsync(page, app, "DEV-BACKEND", "8");

        // Totals arrive over the circuit, with no page load in between.
        await Expect(page.GetByTestId("invoice-gross")).ToContainTextAsync("952");
        await page.GetByTestId("invoice-review").ClickAsync();
        await page.GetByTestId("invoice-confirm").ClickAsync();

        await Expect(page.GetByTestId("invoice-number")).ToHaveTextAsync(InvoiceNumber());
    }

    [Fact]
    public async Task A_german_user_enters_a_decimal_quantity_with_a_comma()
    {
        // Spec section 9, review M14: the circuit parses with the culture it started with.
        IPage english = await app.SignInAsAdministratorAsync(_browser!);
        await EnsureMasterDataAsync(english, app);

        IPage page = await app.SignInAsAdministratorAsync(_browser!, AuthenticatedWebAppFixture.GermanLocale);
        await DraftAsync(page, app, "DEV-BACKEND", "8,5");

        await Expect(page.GetByTestId("invoice-net")).ToContainTextAsync("850,00");

        // Reloaded from numeric(18,4), whose scale Npgsql keeps: without a format the field
        // would read back "8,5000".
        await page.ReloadAsync();
        await Expect(page.GetByTestId("invoice-quantity")).ToHaveValueAsync("8,5");
    }

    [Fact]
    public async Task A_german_user_enters_a_decimal_price_with_a_comma()
    {
        // Review M14 for the price: the catalog page parses it with the circuit's culture.
        IPage page = await app.SignInAsAdministratorAsync(_browser!, AuthenticatedWebAppFixture.GermanLocale);
        await page.GotoAsync(app.Url("/catalog"));
        await page.GetByTestId("catalog-number").FillAsync("PRICE-DE");
        await page.GetByTestId("catalog-name").FillAsync("Preisprobe");
        await page.GetByTestId("catalog-price").FillAsync("100,50");
        await page.GetByTestId("catalog-save").ClickAsync();

        await Expect(page.Locator("tr", new() { Has = page.GetByTestId("catalog-row").Filter(new() { HasText = "PRICE-DE" }) }))
            .ToContainTextAsync("100,50 EUR");
        (await app.ReadCatalogItemUnitPriceAsync("PRICE-DE")).Should().Be(100.50m);

        // Reloaded, the stored scale comes back with the value: "100,5000" and "19,00"
        // without a format on the fields.
        await page.ReloadAsync();
        await page.GetByTestId("catalog-row").Filter(new() { HasText = "PRICE-DE" }).ClickAsync();
        await Expect(page.GetByTestId("catalog-number")).ToHaveValueAsync("PRICE-DE");
        await Expect(page.GetByTestId("catalog-price")).ToHaveValueAsync("100,5");
        await Expect(page.GetByTestId("catalog-vat-rate")).ToHaveValueAsync("19");
    }

    [Fact]
    public async Task A_double_click_on_new_creates_one_draft()
    {
        // Stage 1 has no draft delete, so a second draft from one double click would stay.
        IPage page = await app.SignInAsAdministratorAsync(_browser!);
        await EnsureMasterDataAsync(page, app);
        await page.GotoAsync(app.Url("/invoices"));
        await ChooseAsync(page, "invoice-new-customer", "Example Client GmbH");
        int before = await app.CountInvoicesAsync();

        await page.GetByTestId("invoice-new").DblClickAsync();

        // Counted once the draft page has loaded, which takes several operations longer than
        // a second CreateDraft would.
        await Expect(page.GetByTestId("invoice-catalog-item").Filter(new() { Visible = true })).ToBeVisibleAsync();
        (await app.CountInvoicesAsync()).Should().Be(before + 1);
    }

    [Fact]
    public async Task An_edited_numbered_invoice_is_confirmed_again_under_its_number()
    {
        // Spec section 4: a numbered invoice's data stays editable. The edit clears its
        // figures, the page asks for a new confirmation, and finalization keeps the number.
        IPage page = await app.SignInAsAdministratorAsync(_browser!);
        await EnsureMasterDataAsync(page, app);
        await DraftAsync(page, app, "DEV-BACKEND", "8");
        await page.GetByTestId("invoice-review").ClickAsync();
        await page.GetByTestId("invoice-confirm").ClickAsync();
        await Expect(page.GetByTestId("invoice-number")).ToHaveTextAsync(InvoiceNumber());
        await Expect(page.GetByTestId("invoice-due-date")).ToBeVisibleAsync();
        string number = await page.GetByTestId("invoice-number").InnerTextAsync();

        await page.GetByTestId("invoice-quantity").FillAsync("9");
        await page.GetByTestId("invoice-quantity").PressAsync("Tab");

        await Expect(page.GetByTestId("invoice-needs-confirmation")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("invoice-due-date")).ToBeHiddenAsync();
        await Expect(page.GetByTestId("invoice-gross")).ToHaveTextAsync("1,071.00");
        await page.GetByTestId("invoice-review").ClickAsync();
        await page.GetByTestId("invoice-confirm").ClickAsync();

        await Expect(page.GetByTestId("invoice-needs-confirmation")).ToBeHiddenAsync();
        await Expect(page.GetByTestId("invoice-review")).ToBeHiddenAsync();
        await Expect(page.GetByTestId("invoice-number")).ToHaveTextAsync(number);
        await Expect(page.GetByTestId("invoice-gross")).ToHaveTextAsync("1,071.00");
        await Expect(page.GetByTestId("invoice-due-date")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task An_edit_refused_because_another_tab_numbered_the_invoice_says_so()
    {
        // SaveDraft takes the invoice's row lock and the aggregate refuses a new issue date
        // once a number exists. This tab still shows a draft; the edit must not end the
        // circuit, and the page must show the invoice as it now is.
        IPage page = await app.SignInAsAdministratorAsync(_browser!);
        await EnsureMasterDataAsync(page, app);
        await DraftAsync(page, app, "DEV-BACKEND", "8");
        await Expect(page.GetByTestId("invoice-gross")).ToContainTextAsync("952");

        IPage other = await app.SignInAsAdministratorAsync(_browser!);
        await other.GotoAsync(page.Url);
        await other.GetByTestId("invoice-review").ClickAsync();
        await other.GetByTestId("invoice-confirm").ClickAsync();
        await Expect(other.GetByTestId("invoice-number")).ToHaveTextAsync(InvoiceNumber());
        string number = await other.GetByTestId("invoice-number").InnerTextAsync();

        // The draft's issue date is today in the organization's time zone; pick another day.
        DateTimeOffset berlin = TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        await page.Locator("input[data-testid=invoice-issue-date]").ClickAsync();
        await page.Locator(".mud-picker-calendar-day:not(.mud-adjacent-month)")
            .GetByText(berlin.Day == 1 ? "2" : "1", new() { Exact = true })
            .ClickAsync();

        await Expect(page.GetByTestId("invoice-changed-elsewhere")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("invoice-number")).ToHaveTextAsync(number);
        await Expect(page.Locator("input[data-testid=invoice-issue-date]")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task Totals_that_changed_since_the_review_are_confirmed_again()
    {
        // Review M12: finalization numbers only the gross the user confirmed. A price changed
        // between the review and the confirmation is refused, and the panel stays open over
        // the new totals for a second confirmation.
        IPage page = await app.SignInAsAdministratorAsync(_browser!);
        await EnsureMasterDataAsync(page, app);
        await SaveCatalogItemAsync(page, app, "PRICE-PROBE", "50");
        await DraftAsync(page, app, "PRICE-PROBE", "2");
        await page.GetByTestId("invoice-review").ClickAsync();
        await Expect(page.GetByTestId("invoice-confirm-gross")).ToHaveTextAsync("119.00");

        IPage catalog = await app.SignInAsAdministratorAsync(_browser!);
        await SaveCatalogItemAsync(catalog, app, "PRICE-PROBE", "60");

        await page.GetByTestId("invoice-confirm").ClickAsync();
        await Expect(page.GetByTestId("invoice-refusal")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("invoice-confirm-gross")).ToHaveTextAsync("142.80");
        await Expect(page.GetByTestId("invoice-number")).ToHaveTextAsync(string.Empty);

        await page.GetByTestId("invoice-confirm").ClickAsync();
        await Expect(page.GetByTestId("invoice-number")).ToHaveTextAsync(InvoiceNumber());
        await Expect(page.GetByTestId("invoice-gross")).ToHaveTextAsync("142.80");
    }

    // private static Methods
    /// <summary>
    /// Completes the organization and creates the walking skeleton's item, customer, project
    /// and customer catalog item number, skipping whatever an earlier test in this serial
    /// assembly already created. Opening a select goes through MudPopoverProvider, so this
    /// fails if the providers are back in the static layout (review C9).
    /// </summary>
    private static async Task EnsureMasterDataAsync(IPage page, AuthenticatedWebAppFixture app)
    {
        await page.GotoAsync(app.Url("/organization"));
        await page.GetByTestId("organization-legal-name").FillAsync("Example Consulting");
        await page.GetByTestId("organization-country").FillAsync("DE");
        await page.GetByTestId("organization-vat-id").FillAsync("DE123456789");
        await page.GetByTestId("organization-iban").FillAsync("DE89370400440532013000");
        await page.GetByTestId("organization-currency").FillAsync("EUR");
        if (await page.GetByTestId("organization-scheme-locked").CountAsync() == 0)
        {
            await page.GetByTestId("organization-prefix").FillAsync("R");
            await ChooseAsync(page, "organization-reset-scope", "Per day");
        }

        await page.GetByTestId("organization-save").ClickAsync();
        await Expect(page.GetByTestId("organization-saved")).ToBeVisibleAsync();

        await page.GotoAsync(app.Url("/catalog"));
        await page.GetByTestId("catalog-save").WaitForAsync();
        if (await page.GetByTestId("catalog-row").Filter(new() { HasText = "DEV-BACKEND" }).CountAsync() == 0)
        {
            await SaveCatalogItemAsync(page, app, "DEV-BACKEND", "100");
        }

        await page.GotoAsync(app.Url("/customers"));
        await page.GetByTestId("customer-save").WaitForAsync();
        if (await page.GetByTestId("customer-row").Filter(new() { HasText = "C-4711" }).CountAsync() == 0)
        {
            await page.GetByTestId("customer-legal-name").FillAsync("Example Client GmbH");
            await page.GetByTestId("customer-country").FillAsync("DE");
            await page.GetByTestId("customer-number").FillAsync("C-4711");
            await page.GetByTestId("customer-save").ClickAsync();

            await page.GetByTestId("project-reference").FillAsync("PRJ-2026-083");
            await page.GetByTestId("project-add").ClickAsync();
            await Expect(page.GetByTestId("project-row")).ToHaveTextAsync("PRJ-2026-083");

            await ChooseAsync(page, "reference-catalog-item", "DEV-BACKEND");
            await page.GetByTestId("reference-number").FillAsync("SI-9001");
            await page.GetByTestId("reference-save").ClickAsync();
            await Expect(page.Locator("tr", new() { Has = page.GetByTestId("reference-row") }))
                .ToContainTextAsync("SI-9001");
        }
    }

    /// <summary>Creates the item, or reprices it when it exists, through the catalog page.</summary>
    private static async Task SaveCatalogItemAsync(IPage page, AuthenticatedWebAppFixture app, string number, string price)
    {
        await page.GotoAsync(app.Url("/catalog"));
        await page.GetByTestId("catalog-save").WaitForAsync();
        ILocator row = page.GetByTestId("catalog-row").Filter(new() { HasText = number });
        if (await row.CountAsync() > 0)
        {
            await row.ClickAsync();
            await Expect(page.GetByTestId("catalog-number")).ToHaveValueAsync(number);
        }
        else
        {
            await page.GetByTestId("catalog-number").FillAsync(number);
            await page.GetByTestId("catalog-name").FillAsync($"{number} service");
            await page.GetByTestId("catalog-unit").FillAsync("HUR");
            await page.GetByTestId("catalog-currency").FillAsync("EUR");
            await page.GetByTestId("catalog-vat-rate").FillAsync("19");
        }

        await page.GetByTestId("catalog-price").FillAsync(price);
        await page.GetByTestId("catalog-save").ClickAsync();
        await Expect(page.Locator("tr", new() { Has = row })).ToContainTextAsync($"{price}.00 EUR");
    }

    /// <summary>Creates a draft for Example Client GmbH and enters its one line.</summary>
    private static async Task DraftAsync(IPage page, AuthenticatedWebAppFixture app, string itemNumber, string quantity)
    {
        await page.GotoAsync(app.Url("/invoices"));
        await ChooseAsync(page, "invoice-new-customer", "Example Client GmbH");
        await page.GetByTestId("invoice-new").ClickAsync();
        await ChooseAsync(page, "invoice-catalog-item", itemNumber);
        await page.GetByTestId("invoice-quantity").FillAsync(quantity);
        await page.GetByTestId("invoice-quantity").PressAsync("Tab");
    }

    /// <summary>
    /// Opens a MudSelect and picks an option. The visible box only: with a value, the select
    /// also renders a hidden input carrying the same <c>data-testid</c>.
    /// </summary>
    private static async Task ChooseAsync(IPage page, string testId, string option)
    {
        await page.GetByTestId(testId).Filter(new() { Visible = true }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
    }

    /// <summary>
    /// Prefix R, the PerDay date part, then the padded counter. No RegexOptions: Playwright
    /// hands the pattern to the browser and refuses a flag JavaScript has no equivalent for.
    /// </summary>
    [GeneratedRegex("^R[0-9]{6}[0-9]+$")]
    private static partial Regex InvoiceNumber();
}
