using System.Net;
using System.Security.Claims;
using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// The real application on a real socket in front of a real PostgreSQL, built through
/// <see cref="FakturennWebApplication.Build"/> — the same composition the host uses,
/// including forwarded headers, the security-header middleware, the rate limiter and
/// <c>UseAntiforgery</c>.
/// <para>
/// A test that constructed a <c>UserManager</c> by hand and called it would prove the
/// Identity library works and say nothing about whether the endpoint is mapped, whether
/// the pipeline lets the request through, or whether the configured password policy is
/// the one that runs. <c>/account/setup</c> is an unauthenticated endpoint that mints an
/// administrator, so nothing less than the real pipeline is worth asserting on.
/// </para>
/// </summary>
public sealed class SetupHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("fakturenn")
        .WithUsername("fakturenn")
        .WithPassword("fakturenn")
        .Build();

    private WebApplication? _app;

    public string BaseAddress { get; private set; } = string.Empty;

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>The running host's container, so a test can borrow its real services.</summary>
    public IServiceProvider Services =>
        _app?.Services ?? throw new InvalidOperationException("The host has not been started.");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // The Data Protection ring is persisted to the database, and antiforgery asks it
        // for a key on the first request, so its schema has to exist before the host
        // starts serving.
        await using (DataProtectionDbContext dataProtection = CreateDataProtectionContext())
        {
            await dataProtection.Database.MigrateAsync();
        }

        await using (IdentityDbContext identity = CreateIdentityContext())
        {
            await identity.Database.MigrateAsync();

            // Seeded here for the same reason --migrate seeds it: DEPLOYMENT-BASELINE.md
            // requires the migration Job to run before the instance serves traffic, so a
            // real /setup post always meets an existing Administrator role.
            //
            // This is not cosmetic. Without it the FIRST concurrent post to reach
            // RoleSeeder inserts the role and holds its uncommitted unique-index entry,
            // which blocks every other racer until it commits and then fails them with a
            // duplicate-key DbUpdateException -- rolling back their user insert as well.
            // That accident makes a race test pass with no setup guard at all, which is
            // exactly the false green this endpoint has already produced once.
            await RoleSeeder.SeedAsync(identity, CancellationToken.None);
        }

        // Configuration through the command line rather than an environment variable:
        // environment variables are process-wide and this test process hosts other
        // suites' containers too.
        _app = FakturennWebApplication.Build(
        [
            "--urls",
            "http://127.0.0.1:0",
            $"--ConnectionStrings:Fakturenn={ConnectionString}",
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

        await _container.DisposeAsync();
    }

    public IdentityDbContext CreateIdentityContext() =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(ConnectionString)
                .Options,
            DataProtectionProvider.Create("Fakturenn.Tests"));

    /// <summary>
    /// Returns the instance to the state <c>/setup</c> exists for: no users, no role
    /// assignments. The seeded system roles are left alone, because a real first run may
    /// meet either state — <c>--migrate</c> seeds them before anyone reaches the page.
    /// </summary>
    public async Task ResetUsersAsync(CancellationToken cancellationToken)
    {
        await using IdentityDbContext context = CreateIdentityContext();

        await context.UserRoles.ExecuteDeleteAsync(cancellationToken);
        await context.Users.ExecuteDeleteAsync(cancellationToken);
    }

    public HttpClient CreateClient() =>
        // Redirects are the assertion, not a detour: /account/login does not exist yet,
        // so following one would turn every success into a 404.
        new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(BaseAddress),
        };

    /// <summary>
    /// A client that keeps cookies in the supplied container, the way a browser does.
    /// <para>
    /// The container is not a convenience. The recovery-code cookie is show-once because
    /// the server sends a deletion for it; a test that simply declined to resend the
    /// cookie would pass whether or not that deletion exists. Only a store that honours
    /// the <c>Set-Cookie</c> expiry can tell the two apart.
    /// </para>
    /// </summary>
    public HttpClient CreateClient(CookieContainer cookies) =>
        new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = cookies,
        })
        {
            BaseAddress = new Uri(BaseAddress),
        };

    /// <summary>
    /// Creates a user through the host's own <see cref="UserManager{TUser}"/>, so the
    /// security stamp, the normalised names and the password hasher are the ones the
    /// running application uses rather than a second set built for the test.
    /// </summary>
    public async Task<ApplicationUser> CreateUserAsync(string email, CancellationToken cancellationToken)
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
            MustEnrolTotp = true,
        };

        cancellationToken.ThrowIfCancellationRequested();

        IdentityResult result = await users.CreateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the test user: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        return user;
    }

    /// <summary>
    /// Mints the application's real authentication cookie for a user.
    /// <para>
    /// Sign-in does not exist until Task 11, so there is no endpoint to post credentials
    /// to yet. Rather than fake authentication with a test scheme — which would prove
    /// nothing about the pipeline that actually guards these pages — this asks the host
    /// for the same claims factory and the same <see cref="CookieAuthenticationOptions"/>
    /// the cookie handler uses, and protects the ticket with the host's own key ring.
    /// The result is a cookie the running application accepts because it is the cookie
    /// the running application would have issued.
    /// </para>
    /// <para>
    /// <c>IssuedUtc</c> is set to now on purpose: the security-stamp validator revalidates
    /// only after its one-minute interval has elapsed, so a freshly issued ticket keeps
    /// these tests independent of how long the suite has been running.
    /// </para>
    /// </summary>
    public async Task<Cookie> CreateAuthenticationCookieAsync(ApplicationUser user)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        IUserClaimsPrincipalFactory<ApplicationUser> factory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        ClaimsPrincipal principal = await factory.CreateAsync(user);

        CookieAuthenticationOptions options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        AuthenticationTicket ticket = new(principal, IdentityConstants.ApplicationScheme);
        ticket.Properties.IssuedUtc = DateTimeOffset.UtcNow;
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1);

        return new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/");
    }

    /// <summary>
    /// How many recovery codes the account currently holds, read through the host's
    /// <see cref="UserManager{TUser}"/> so the encrypted column is decrypted with the
    /// host's key ring rather than the test's.
    /// </summary>
    public async Task<int> CountRecoveryCodesAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser user = await users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"No user with id {userId}.");

        return await users.CountRecoveryCodesAsync(user);
    }

    private DataProtectionDbContext CreateDataProtectionContext() =>
        new(new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);
}
