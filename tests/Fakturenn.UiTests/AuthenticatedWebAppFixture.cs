using AwesomeAssertions;
using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Modules.Invoices.Persistence;
using Fakturenn.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using OtpNet;
using Testcontainers.PostgreSql;

namespace Fakturenn.UiTests;

/// <summary>
/// An account the browser tests sign in as, with the shared secret needed to compute a
/// real RFC 6238 code for it.
/// </summary>
/// <param name="Email">The user name and e-mail address.</param>
/// <param name="Password">The password, accepted by the host's own password policy.</param>
/// <param name="TotpKey">The base32 authenticator key the account holds.</param>
public sealed record UiAccount(string Email, string Password, string TotpKey);

/// <summary>
/// Hosts the real application against a real PostgreSQL container, so the identity
/// journey exercises genuine persistence, genuine Data Protection and genuine RFC 6238
/// verification. Nothing here bypasses two-factor authentication: every code submitted is
/// computed with <c>Otp.NET</c> from the key the application itself issued, and the
/// reusable authenticated state is a cookie the running application minted in response to
/// a password and a correct code.
/// </summary>
public sealed class AuthenticatedWebAppFixture : IAsyncLifetime
{
    // private const Fields

    /// <summary>
    /// Playwright's default is thirty seconds, and the first navigation against a freshly
    /// started host is the slowest request this suite makes: the EF model is built, the
    /// Data Protection ring is created and the Razor components are initialised, all on the
    /// first request rather than at startup. Measured cold: 247 ms for <c>GET /setup</c>
    /// against about 5 ms warm.
    /// <para>
    /// This is margin, NOT a fix for anything. It was first raised on the theory that a
    /// slow cold start explained an intermittent failure; that theory was wrong, and the
    /// two real causes were found and removed instead — see
    /// <c>AssemblyInfo.cs</c> for the EF model race, and <see cref="ArriveAtAsync"/> for the
    /// <c>WaitForURLAsync</c> race. Neither was a timeout problem. If a wait here ever
    /// starts failing again, do not raise this number: something is wrong.
    /// </para>
    /// </summary>
    private const int ColdStartTimeoutMilliseconds = 60_000;

    // private static readonly Fields

    /// <summary>
    /// Every context is created with this locale, so the assertions can name English
    /// strings without depending on the host machine's culture.
    /// </summary>
    private static readonly BrowserNewContextOptions _englishContext = new()
    {
        Locale = "en-GB",
        ExtraHTTPHeaders = new Dictionary<string, string> { ["Accept-Language"] = "en-GB" },
    };

    // private readonly Fields

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("fakturenn")
        .WithUsername("fakturenn")
        .WithPassword("fakturenn")
        .Build();

    /// <summary>
    /// The first-run journey may be requested by several tests at once, and it can only
    /// happen once per instance — <c>/setup</c> closes behind it.
    /// </summary>
    private readonly SemaphoreSlim _firstRun = new(1, 1);

    // private Fields

    private WebApplication? _app;

    /// <summary>
    /// Playwright's serialised cookie jar for the enrolled administrator. This is
    /// SPIKE-009's "reusable authenticated state", and it is deliberately the output of a
    /// genuine sign-in rather than a fabricated cookie.
    /// </summary>
    private string? _administratorStorageState;

    /// <summary>Why the first-run journey failed, if it did. See <see cref="EnsureAdministratorAsync"/>.</summary>
    private Exception? _firstRunFailure;

    // public Properties

    /// <summary>The host's own address, exactly as Kestrel resolved it.</summary>
    public string BaseAddress { get; private set; } = string.Empty;

    public string AdminEmail { get; } = "admin@example.test";

    public string AdminPassword { get; } = "Str0ng!Passw0rd!";

    /// <summary>
    /// The administrator's base32 authenticator key, read from the enrolment page's own
    /// manual-entry field during the first-run journey.
    /// </summary>
    public string TotpSecret { get; private set; } = string.Empty;

    // public Methods

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        await MigrateAsync();

        // Configuration through the command line rather than an environment variable:
        // environment variables are process-wide and this test process hosts other
        // fixtures' applications too.
        //
        // --environment and --applicationName are what make this host serve the
        // application's real static assets, and without them the browser tests are
        // looking at pages on which nothing loads. Measured: with the defaults, /app.css,
        // /Fakturenn.Web.styles.css, /_content/MudBlazor/MudBlazor.min.{css,js},
        // /_framework/blazor.web.js and the layout's scoped script ALL answered 404. The
        // generic host loads the static web assets manifest only in Development, and it
        // looks for "{ApplicationName}.staticwebassets.runtime.json" beside the assembly
        // named by ApplicationName -- which in a test process defaults to the test
        // assembly, not Fakturenn.Web.
        //
        // This matters most to ContentSecurityPolicyTests: a policy check run against a
        // page whose every script and stylesheet 404s would assert that nothing was
        // blocked while nothing was ever fetched.
        _app = FakturennWebApplication.Build(
        [
            "--urls",
            "http://127.0.0.1:0",
            "--environment",
            "Development",
            "--applicationName",
            "Fakturenn.Web",
            $"--ConnectionStrings:Fakturenn={_postgres.GetConnectionString()}",
        ]);

        await _app.StartAsync();

        BaseAddress = _app.Urls.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        await _postgres.DisposeAsync();

        _firstRun.Dispose();
    }

    /// <summary>
    /// An absolute URL for a path on the running host.
    /// <para>
    /// <c>WebApplication.Urls</c> reports the address without a trailing slash
    /// (measured: <c>http://127.0.0.1:&lt;port&gt;</c>), so concatenating a relative path
    /// straight onto it produces <c>…:5000account/login</c>. Everything here goes through
    /// this method rather than interpolating the base address.
    /// </para>
    /// </summary>
    public string Url(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return new Uri(new Uri(BaseAddress, UriKind.Absolute), path).ToString();
    }

    /// <summary>
    /// A browser context pointed at this host: English, and patient enough for a cold
    /// start. Every context in this suite comes from here, so no test can quietly get the
    /// thirty-second default back.
    /// </summary>
    /// <param name="browser">The browser to open the context in.</param>
    /// <param name="storageState">
    /// A serialised Playwright cookie jar to start from, or <see langword="null"/> for a
    /// context that has never signed in.
    /// </param>
    public static async Task<IBrowserContext> NewContextAsync(IBrowser browser, string? storageState = null)
    {
        ArgumentNullException.ThrowIfNull(browser);

        IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = _englishContext.Locale,
            ExtraHTTPHeaders = _englishContext.ExtraHTTPHeaders,
            StorageState = storageState,
        });

        context.SetDefaultTimeout(ColdStartTimeoutMilliseconds);
        context.SetDefaultNavigationTimeout(ColdStartTimeoutMilliseconds);

        return context;
    }

    /// <summary>
    /// Waits until the page has arrived at <paramref name="path"/> and has actually
    /// rendered <paramref name="testId"/>, then asserts the path.
    /// <para>
    /// Never <c>WaitForURLAsync</c> after a click, and this is measured, not stylistic.
    /// <c>ClickAsync</c> returns once the click is dispatched, so the navigation it causes
    /// may finish before or after the next line runs. When it finishes first, Playwright's
    /// <c>WaitForURLAsync</c> sees the URL already matching and falls through to
    /// <c>WaitForLoadStateAsync(Load)</c> — and the new document's <c>load</c> event has
    /// then already fired, so it waits for an event that will never come again. Measured:
    /// the first-run journey hung the full timeout at exactly that line, with the server
    /// log showing <c>POST /account/setup responded 302</c> followed by
    /// <c>GET /account/login responded 200</c> — the navigation the wait was waiting for
    /// had already succeeded — and Playwright's own log reporting only
    /// <c>"NetworkIdle" event fired</c>. Four tests failed on the cascade; nine runs
    /// either side were green.
    /// </para>
    /// <para>
    /// A locator wait has no such race: it re-evaluates until the element is there. The
    /// path assertion is what keeps it honest, because an element alone would not prove
    /// which page rendered it.
    /// </para>
    /// </summary>
    public static async Task ArriveAtAsync(IPage page, string path, string testId)
    {
        ArgumentNullException.ThrowIfNull(page);

        await page.GetByTestId(testId).WaitForAsync();

        new Uri(page.Url).AbsolutePath.Should().Be(path);
    }

    /// <summary>The current RFC 6238 code for the administrator's key.</summary>
    public string CurrentTotpCode() => CodeFor(TotpSecret);

    /// <summary>The current RFC 6238 code for any base32 key.</summary>
    public static string CodeFor(string totpKey) =>
        new Totp(Base32Encoding.ToBytes(totpKey)).ComputeTotp();

    /// <summary>
    /// A six-digit code that is syntactically valid and is not the current one. Derived
    /// from the real code so it cannot collide with it, including across a window roll.
    /// </summary>
    public static string WrongCodeFor(string totpKey) =>
        ((int.Parse(CodeFor(totpKey), System.Globalization.CultureInfo.InvariantCulture) + 500_000) % 1_000_000)
            .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Drives first-run setup, forced enrolment and the recovery-code acknowledgement on
    /// the supplied page, leaving it signed in as the administrator.
    /// <para>
    /// Public because the Content-Security-Policy test walks the same journey on a page it
    /// has instrumented, which is how <c>/setup</c>, <c>/account/enrol-totp</c> and
    /// <c>/account/recovery-codes</c> get looked at with the browser's violation reporting
    /// attached. Calling it twice is a failure, not a no-op: <c>/setup</c> closes
    /// permanently once a user exists.
    /// </para>
    /// </summary>
    public async Task RunFirstRunJourneyAsync(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // 1. Setup exists while no user does.
        await page.GotoAsync(Url("/setup"));
        await page.GetByTestId("setup-email").FillAsync(AdminEmail);
        await page.GetByTestId("setup-display-name").FillAsync("Administrator");
        await page.GetByTestId("setup-password").FillAsync(AdminPassword);
        await page.GetByTestId("setup-submit").ClickAsync();

        // 2. Sign in with the password.
        await ArriveAtAsync(page, "/account/login", "login-form");
        await page.GetByTestId("login-email").FillAsync(AdminEmail);
        await page.GetByTestId("login-password").FillAsync(AdminPassword);
        await page.GetByTestId("login-submit").ClickAsync();

        // 3. Enrolment is forced, and the manual-entry key is the real shared secret.
        await ArriveAtAsync(page, "/account/enrol-totp", "enrol-form");
        string displayedKey = (await page.GetByTestId("totp-key").TextContentAsync())!;
        TotpSecret = displayedKey.Replace(" ", string.Empty, StringComparison.Ordinal);

        await page.GetByTestId("enrol-code").FillAsync(CurrentTotpCode());
        await page.GetByTestId("enrol-submit").ClickAsync();

        // 4. Recovery codes are shown exactly once.
        await ArriveAtAsync(page, "/account/recovery-codes", "recovery-codes");
        string codes = (await page.GetByTestId("recovery-codes").TextContentAsync())!;
        codes.Should().NotBeNullOrWhiteSpace("the enrolment must issue recovery codes");

        await page.GetByTestId("recovery-continue").ClickAsync();
        await page.GetByTestId("app-tagline").WaitForAsync();
    }

    /// <summary>
    /// A page already signed in as the enrolled administrator.
    /// <para>
    /// The first caller pays for the real journey — first-run setup, the password, a code
    /// computed from the key the application displayed — and the resulting cookie jar is
    /// serialised and replayed into a fresh browser context for every caller after that.
    /// Reusing the state is an optimisation; producing it any other way than by signing in
    /// would make every test built on it prove nothing about authentication.
    /// </para>
    /// </summary>
    public async Task<IPage> SignInAsAdministratorAsync(IBrowser browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        await EnsureAdministratorAsync(browser);

        IBrowserContext context = await NewContextAsync(browser, _administratorStorageState);

        return await context.NewPageAsync();
    }

    /// <summary>
    /// Runs the first-run journey once, so a test whose subject is not the journey can
    /// still depend on an administrator existing without depending on test order.
    /// <para>
    /// A failure is remembered and re-thrown rather than retried. The journey can only
    /// succeed once — <c>/setup</c> closes as soon as the user row exists — so a second
    /// attempt after a failed first one lands on the sign-in page and times out waiting for
    /// <c>setup-email</c>, which says nothing about what actually went wrong. Measured: one
    /// real failure produced four red tests, three of them reporting a missing
    /// <c>setup-email</c> field on a page that was never supposed to be shown again.
    /// </para>
    /// </summary>
    public async Task EnsureAdministratorAsync(IBrowser browser)
    {
        ArgumentNullException.ThrowIfNull(browser);

        await _firstRun.WaitAsync();

        try
        {
            if (_firstRunFailure is not null)
            {
                throw new InvalidOperationException(
                    "The first-run journey already failed on this fixture; every later test in "
                    + "this collection depends on it. The original failure is the inner exception.",
                    _firstRunFailure);
            }

            if (_administratorStorageState is not null)
            {
                return;
            }

            IBrowserContext context = await NewContextAsync(browser);
            IPage page = await context.NewPageAsync();

            try
            {
                await RunFirstRunJourneyAsync(page);
            }
            catch (Exception failure)
            {
                _firstRunFailure = failure;
                throw;
            }

            _administratorStorageState = await context.StorageStateAsync();

            await context.CloseAsync();
        }
        finally
        {
            _firstRun.Release();
        }
    }

    /// <summary>
    /// Creates an account that holds no role at all, already enrolled in two-factor
    /// authentication, and returns the credentials needed to sign in as it.
    /// <para>
    /// No role means no permission, which is the point: it is the subject of the test that
    /// an authenticated caller without <c>users.read</c> is turned away from
    /// <c>/admin/users</c>. Everything is done through the host's own
    /// <see cref="UserManager{TUser}"/>, so the password hasher, the normalised names and
    /// the security stamp are the running application's rather than a second set built for
    /// the test.
    /// </para>
    /// </summary>
    public async Task<UiAccount> CreateEnrolledUserAsync(string email, string password)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = email,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        IdentityResult created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the test user: {string.Join("; ", created.Errors.Select(error => error.Description))}");
        }

        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);

        string key = await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("The authenticator key was not stored.");

        return new UiAccount(email, password, key);
    }

    /// <summary>
    /// Signs in through the real pages: the password form, then the two-factor challenge
    /// with a code computed from the account's own key. Returns the signed-in page.
    /// </summary>
    public async Task<IPage> SignInAsync(IBrowser browser, UiAccount account)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(account);

        IBrowserContext context = await NewContextAsync(browser);
        IPage page = await context.NewPageAsync();

        await page.GotoAsync(Url("/account/login"));
        await page.GetByTestId("login-email").FillAsync(account.Email);
        await page.GetByTestId("login-password").FillAsync(account.Password);
        await page.GetByTestId("login-submit").ClickAsync();

        await ArriveAtAsync(page, "/account/login-2fa", "twofa-form");
        await page.GetByTestId("twofa-code").FillAsync(CodeFor(account.TotpKey));
        await page.GetByTestId("twofa-submit").ClickAsync();

        await page.GetByTestId("app-tagline").WaitForAsync();

        return page;
    }

    /// <summary>
    /// Locks an account exactly the way <c>POST /account/admin/set-lockout</c> does,
    /// including the explicit <see cref="UserManager{TUser}.UpdateSecurityStampAsync"/>.
    /// <para>
    /// Writing the column by hand would skip the rotation, and the test that watches a
    /// locked user's session end would then pass on an application that never ends it —
    /// the stamp validator compares the cookie's stamp against the stored one, and an
    /// unrotated stamp still matches.
    /// </para>
    /// </summary>
    public async Task LockUserAsync(string email)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user with the e-mail address {email}.");

        IdentityResult locked = await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        if (!locked.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not lock the user: {string.Join("; ", locked.Errors.Select(error => error.Description))}");
        }

        await users.UpdateSecurityStampAsync(user);
    }

    // private Properties

    /// <summary>The running host's container, so a test can borrow its real services.</summary>
    private IServiceProvider Services =>
        _app?.Services ?? throw new InvalidOperationException("The host has not been started.");

    // private Methods

    /// <summary>
    /// Applies every migration before the host starts serving, because migrations never
    /// run as a side effect of startup and the Data Protection ring is asked for a key by
    /// antiforgery on the very first request.
    /// </summary>
    private async Task MigrateAsync()
    {
        string connectionString = _postgres.GetConnectionString();

        await using (DataProtectionDbContext dataProtection = new(
            new DbContextOptionsBuilder<DataProtectionDbContext>().UseNpgsql(connectionString).Options))
        {
            await dataProtection.Database.MigrateAsync();
        }

        // A file-backed provider, for the same reason the --migrate entrypoint uses one:
        // migrating only builds the model, the value converter is never invoked, and the
        // registered provider persists its ring to a table that may not exist yet.
        await using (IdentityDbContext identity = new(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options,
            DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(Path.GetTempPath(), "fakturenn-ui-tests")))))
        {
            await identity.Database.MigrateAsync();

            // Seeded here for the same reason --migrate seeds it: the migration Job runs
            // before the instance serves traffic, so a real /setup post always meets an
            // existing Administrator role.
            await RoleSeeder.SeedAsync(identity, CancellationToken.None);
        }

        await using InvoicesDbContext invoices = new(
            new DbContextOptionsBuilder<InvoicesDbContext>().UseNpgsql(connectionString).Options);

        await invoices.Database.MigrateAsync();
    }
}
