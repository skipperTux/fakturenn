using System.Net;
using System.Net.Sockets;
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
    /// A cookie-keeping client whose connections originate from <paramref name="client"/>,
    /// an address inside the loopback <c>127.0.0.0/8</c> block that Linux assigns to
    /// <c>lo</c> in its entirety.
    /// <para>
    /// This exists because the <c>account</c> rate limiter partitions on
    /// <c>Connection.RemoteIpAddress</c> plus the <c>email</c> form field, and the
    /// second-factor, change-password and sign-out forms carry no e-mail — so for those
    /// endpoints the partition is the client address alone, and ten posts a minute is the
    /// budget for every caller sharing it. Without a distinct source address per test the
    /// suite exhausts one partition and later tests answer 429.
    /// </para>
    /// <para>
    /// Distinct addresses are the honest arrangement rather than a workaround: these tests
    /// are logically distinct clients. Defeating the limiter — raising the permit count or
    /// exempting the endpoints — would stop the suite exercising the pipeline it claims to
    /// exercise.
    /// </para>
    /// </summary>
    public HttpClient CreateClient(CookieContainer cookies, IPAddress client) =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = cookies,
            ConnectCallback = async (context, cancellationToken) =>
            {
                Socket socket = new(client.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };

                try
                {
                    socket.Bind(new IPEndPoint(client, 0));
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        })
        {
            BaseAddress = new Uri(BaseAddress),
        };

    /// <summary>
    /// Creates a user through the host's own <see cref="UserManager{TUser}"/>, so the
    /// security stamp, the normalised names and the password hasher are the ones the
    /// running application uses rather than a second set built for the test.
    /// </summary>
    public Task<ApplicationUser> CreateUserAsync(string email, CancellationToken cancellationToken) =>
        CreateUserAsync(email, password: null, cancellationToken);

    /// <inheritdoc cref="CreateUserAsync(string, CancellationToken)"/>
    /// <param name="email">The user name and e-mail address.</param>
    /// <param name="password">
    /// The password, hashed by the host's configured hasher and validated by the host's
    /// configured policy — so a password this method accepts is one the sign-in endpoint
    /// will accept too.
    /// </param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    public async Task<ApplicationUser> CreateUserAsync(
        string email,
        string? password,
        CancellationToken cancellationToken)
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

        IdentityResult result = password is null
            ? await users.CreateAsync(user)
            : await users.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the test user: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        return user;
    }

    /// <summary>
    /// Puts a user in the state sign-in expects of an enrolled account: an authenticator
    /// key, <c>TwoFactorEnabled</c> set, and the enrolment flag cleared. Returns the
    /// base32 key so the test can compute real RFC 6238 codes from it.
    /// <para>
    /// Driven through the host's <see cref="UserManager{TUser}"/> rather than by writing
    /// columns, so the security stamp rotates exactly as it does in production.
    /// </para>
    /// </summary>
    public async Task<string> EnableTwoFactorAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser user = await FindAsync(users, userId);

        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);

        user.MustEnrolTotp = false;
        await users.UpdateAsync(user);

        return await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("The authenticator key was not stored.");
    }

    /// <summary>Issues recovery codes through the host, and returns them in plaintext.</summary>
    public async Task<string[]> GenerateRecoveryCodesAsync(Guid userId, int count)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser user = await FindAsync(users, userId);

        IEnumerable<string> codes =
            await users.GenerateNewTwoFactorRecoveryCodesAsync(user, count)
            ?? throw new InvalidOperationException("No recovery codes were issued.");

        return [.. codes];
    }

    /// <summary>
    /// Sets <c>MustChangePassword</c> by column update rather than through
    /// <see cref="UserManager{TUser}"/>, so the security stamp is left alone — a test
    /// arranging this flag must not incidentally invalidate the cookie it is about to use.
    /// </summary>
    public async Task SetMustChangePasswordAsync(Guid userId, bool value, CancellationToken cancellationToken)
    {
        await using IdentityDbContext context = CreateIdentityContext();

        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(user => user.MustChangePassword, value),
                cancellationToken);
    }

    /// <summary>The user's current security stamp, read straight from the store.</summary>
    public async Task<string> ReadSecurityStampAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using IdentityDbContext context = CreateIdentityContext();

        return await context.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp!)
            .SingleAsync(cancellationToken);
    }

    /// <summary>The name of the application authentication cookie this host issues.</summary>
    public string ApplicationCookieName => ApplicationCookieOptions().Cookie.Name!;

    /// <summary>
    /// The security-stamp claim carried by an authentication cookie, decrypted with the
    /// host's own ticket format.
    /// <para>
    /// This is the mechanism the one-minute
    /// <c>SecurityStampValidatorOptions.ValidationInterval</c> checks: on the next
    /// revalidation the handler compares this claim against the user's stored stamp and
    /// signs the session out when they differ. Reading the claim proves the property
    /// without waiting a minute for the validator to act on it.
    /// </para>
    /// </summary>
    public string? ReadSecurityStampClaim(string protectedCookieValue)
    {
        AuthenticationTicket? ticket = ApplicationCookieOptions().TicketDataFormat
            .Unprotect(protectedCookieValue);

        IdentityOptions identity = Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        return ticket?.Principal.FindFirstValue(identity.ClaimsIdentity.SecurityStampClaimType);
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

        CookieAuthenticationOptions options = ApplicationCookieOptions();

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

    private static async Task<ApplicationUser> FindAsync(UserManager<ApplicationUser> users, Guid userId) =>
        await users.FindByIdAsync(userId.ToString())
        ?? throw new InvalidOperationException($"No user with id {userId}.");

    private CookieAuthenticationOptions ApplicationCookieOptions() =>
        Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

    private DataProtectionDbContext CreateDataProtectionContext() =>
        new(new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);
}
