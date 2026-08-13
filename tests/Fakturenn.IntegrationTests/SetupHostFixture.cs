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

            // Index 1, because appsettings.json already occupies index 0 with the console
            // sink. Adding a sink rather than replacing one keeps the host's real logging
            // configuration in play: HostLogCapture observes what the application writes, it
            // does not stand in for the pipeline that writes it.
            "--Serilog:WriteTo:1:Name=Sink",
            $"--Serilog:WriteTo:1:Args:sink={HostLogCapture.ConfigurationName}",
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
    /// The <c>account</c> rate limiter partitions on the caller's identity <i>plus</i> the
    /// client address (<c>AccountRateLimitPartition</c>), so distinct users no longer share
    /// a budget merely by sharing an address. This overload therefore exists for the one
    /// suite that wants the opposite: <c>AccountRateLimitTests</c> pins several clients to
    /// <b>one</b> address deliberately, because whether they then share a budget is the
    /// property under test.
    /// </para>
    /// <para>
    /// It is not a workaround for a limiter that is in the way. Defeating the limiter —
    /// raising the permit count or exempting the endpoints — would stop the suite exercising
    /// the pipeline it claims to exercise.
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
    /// Mints the application's real authentication cookie for a user, without going through
    /// the sign-in endpoints.
    /// <para>
    /// The endpoints exist, and a test whose subject is sign-in itself should use them.
    /// This is for the tests whose subject is what happens <i>after</i> a session exists:
    /// it skips the two-factor exchange without faking authentication with a test scheme,
    /// by asking the host for the same claims factory and the same
    /// <see cref="CookieAuthenticationOptions"/> the cookie handler uses and protecting the
    /// ticket with the host's own key ring. The result is a cookie the running application
    /// accepts because it is the cookie the running application would have issued.
    /// </para>
    /// <para>
    /// The claims come from the <paramref name="user"/> instance passed in, security stamp
    /// included. Rotate the stamp — enable two-factor, reset a password — and then mint a
    /// cookie from a stale instance, and the validator rejects it on its first
    /// revalidation.
    /// </para>
    /// <para>
    /// <c>IssuedUtc</c> is set to now, which keeps these tests independent of how long the
    /// suite has been running: the security-stamp validator revalidates only once its
    /// one-minute interval has elapsed since the ticket was issued.
    /// </para>
    /// </summary>
    public Task<Cookie> CreateAuthenticationCookieAsync(ApplicationUser user) =>
        CreateAuthenticationCookieAsync(user, DateTimeOffset.UtcNow);

    /// <inheritdoc cref="CreateAuthenticationCookieAsync(ApplicationUser)"/>
    /// <param name="user">The user the ticket identifies.</param>
    /// <param name="issuedUtc">
    /// When the ticket was issued. Pass a time further back than
    /// <c>SecurityStampValidatorOptions.ValidationInterval</c> to reach the state a real
    /// session reaches a minute after sign-in — one where the next request revalidates the
    /// security stamp instead of trusting the ticket. Without that, a test about ending a
    /// session cannot observe anything.
    /// </param>
    public async Task<Cookie> CreateAuthenticationCookieAsync(ApplicationUser user, DateTimeOffset issuedUtc)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        IUserClaimsPrincipalFactory<ApplicationUser> factory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        ClaimsPrincipal principal = await factory.CreateAsync(user);

        CookieAuthenticationOptions options = ApplicationCookieOptions();

        AuthenticationTicket ticket = new(principal, IdentityConstants.ApplicationScheme);
        ticket.Properties.IssuedUtc = issuedUtc;
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1);

        return new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/");
    }

    /// <summary>
    /// Puts a user in the seeded Administrator role, which is how a test gives one the
    /// <c>users.read</c> and <c>users.manage</c> permissions the administration pages
    /// require.
    /// <para>
    /// Assign before signing in. Permissions are claims minted into the authentication
    /// cookie at sign-in, so a role granted afterwards does not reach an existing session
    /// until the security stamp is revalidated.
    /// </para>
    /// </summary>
    public async Task AssignAdministratorRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using IdentityDbContext context = CreateIdentityContext();

        Guid roleId = await context.Roles
            .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);

        context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The authenticator key the account currently holds, read through the host's
    /// <see cref="UserManager{TUser}"/> — the same value the enrolment page displays.
    /// </summary>
    public async Task<string> ReadAuthenticatorKeyAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser user = await FindAsync(users, userId);

        return await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("The account holds no authenticator key.");
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
