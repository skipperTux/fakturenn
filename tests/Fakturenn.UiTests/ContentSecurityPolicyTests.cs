using System.Globalization;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.Playwright;

namespace Fakturenn.UiTests;

/// <summary>
/// The test the Content-Security-Policy header was shipped without. Task 7C set the policy
/// and said in the code that it was a guess until something proved it; this is that
/// something.
/// <para>
/// A policy that blocks the application's own assets produces symptoms that read as
/// unrelated bugs: a page renders but never becomes interactive, a form posts nothing,
/// styles silently do not apply. Asserting that the header exists cannot see any of that —
/// it passes just as happily for a policy that blocks every script on the page. So this
/// asks the browser instead, through all three channels a block shows up on: the
/// <c>securitypolicyviolation</c> DOM event, the console error Chromium writes beside it,
/// and the failed request.
/// </para>
/// <para>
/// This class takes a fixture of its own rather than joining
/// <see cref="SharedIdentityHost"/>, because <c>/setup</c> is one of the pages under
/// test and it exists only while no user does. Everything happens in one test method for
/// the same reason: the order of the visits is the point, and xUnit does not order tests.
/// </para>
/// </summary>
public sealed class ContentSecurityPolicyTests(AuthenticatedWebAppFixture app)
    : IClassFixture<AuthenticatedWebAppFixture>, IAsyncLifetime
{
    // private static readonly Fields

    /// <summary>The assets a page must actually have fetched for a clean run to mean anything.</summary>
    private static readonly string[] _requiredAssets =
    [
        "/_framework/blazor.web.js",
        "/_content/MudBlazor/MudBlazor.min.js",
        "/_content/MudBlazor/MudBlazor.min.css",
        "/app.css",
    ];

    // private readonly Fields

    private readonly List<string> _violations = [];

    /// <summary>
    /// Every script, stylesheet or font the browser actually received a 2xx for. Without
    /// this the test is vacuous in the most dangerous direction: a policy that blocked
    /// everything and a host that served nothing look identical from the violation list
    /// alone, and both report "no violations".
    /// </summary>
    private readonly List<string> _fetched = [];

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
    public async Task The_content_security_policy_blocks_nothing_the_application_needs()
    {
        IBrowserContext context = await AuthenticatedWebAppFixture.NewContextAsync(_browser!);

        await InstrumentAsync(context);

        IPage page = await context.NewPageAsync();

        // The header must actually be sent, and it must be OURS. The application emits two
        // Content-Security-Policy headers -- this one, and a frame-ancestors 'self' policy
        // from Microsoft.AspNetCore.Components.Server. Browsers enforce multiple policies
        // independently, so the intersection applies and the stricter frame-ancestors
        // wins; an assertion that counted the headers would fail for a reason that has
        // nothing to do with whether this policy is right.
        IResponse? response = await page.GotoAsync(app.Url("/setup"));
        response!.Headers.Should().ContainKey("content-security-policy");
        response.Headers["content-security-policy"].Should().Contain(
            "frame-ancestors 'none'", "the application's own policy must be one of the policies in force");

        // /setup while no user exists, then the whole first-run journey -- which walks
        // /account/login, /account/enrol-totp (the authenticator key) and
        // /account/recovery-codes -- and finally the MudBlazor table on /admin/users.
        // Different pages pull different assets, and a policy that works on the login form
        // and breaks the administration table is exactly what a single-page check misses.
        await page.GetByTestId("setup-form").WaitForAsync();

        await app.RunFirstRunJourneyAsync(page);

        IResponse? admin = await page.GotoAsync(app.Url("/admin/users"));
        admin!.Status.Should().Be(200);
        await page.GetByTestId("user-table").WaitForAsync();

        // The violation report is asynchronous -- the DOM event crosses an exposed binding
        // -- so give an in-flight one a chance to arrive rather than racing the assertion.
        await page.WaitForTimeoutAsync(500);

        // Asserted as "which of the required assets is missing" rather than "does the
        // fetched list contain each one": the fetched list is thirty URLs long, and the
        // runner truncates a long failure message -- which would hide the second assertion
        // behind the first one's evidence dump.
        string[] missing =
        [
            .. _requiredAssets.Where(asset =>
                !_fetched.Exists(url => url.Contains(asset, StringComparison.Ordinal))),
        ];

        // Both halves report, rather than the first failure hiding the second. They are two
        // different diagnoses -- "the policy blocked something" and "nothing was ever
        // fetched, so the clean violation list is meaningless" -- and a run that narrows
        // the policy hits both at once. Measured with script-src 'none': the console
        // channel named the blocked script AND the vacuity guard fired, because Chromium
        // refuses the request rather than issuing one that fails.
        using var scope = new AssertionScope();

        missing.Should().BeEmpty(
            "the walk must actually have loaded these, or a clean violation list means nothing");

        _violations.Should().BeEmpty(
            "the policy must not block the application's own scripts, styles, fonts or connections");
    }

    // private Methods

    /// <summary>
    /// Attaches the three channels a Content-Security-Policy block shows up on.
    /// <para>
    /// Three rather than one because each has a blind spot. The DOM event carries the
    /// directive and the blocked URI but is delivered asynchronously and can be lost across
    /// a navigation. The console error is synchronous but is only text, and a future
    /// Chromium could word it differently. A blocked subresource also surfaces as a failed
    /// request, which catches a block on something that never reaches a document listener
    /// at all.
    /// </para>
    /// </summary>
    private async Task InstrumentAsync(IBrowserContext context)
    {
        await context.ExposeFunctionAsync<string, bool>("fakturennReportCspViolation", report =>
        {
            _violations.Add(report);
            return true;
        });

        await context.AddInitScriptAsync(@"
            document.addEventListener('securitypolicyviolation', event => {
                window.fakturennReportCspViolation(
                    'securitypolicyviolation on ' + event.documentURI +
                    ': ' + event.effectiveDirective + ' blocked ' + event.blockedURI);
            });");

        context.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
            {
                _violations.Add($"console: {message.Text}");
            }
        };

        context.Response += (_, response) =>
        {
            // What the browser actually got, for the anti-vacuity check. Only successes
            // count: a 404 for a stylesheet is a host problem, and treating it as "fetched"
            // would let the vacuity guard pass on a page that loaded nothing.
            if (response.Status is >= 200 and < 300)
            {
                _fetched.Add(response.Url);
            }
        };

        context.RequestFailed += (_, request) =>
        {
            // Only a CSP refusal, not every failure. Filtering on the failure text keeps an
            // unrelated network error from being reported as a policy violation; a blocked
            // subresource fails with ERR_BLOCKED_BY_CSP.
            string failure = request.Failure ?? string.Empty;
            if (failure.Contains("CSP", StringComparison.OrdinalIgnoreCase)
                || failure.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
            {
                _violations.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"request failed: {request.Url} ({failure})"));
            }
        };
    }
}
