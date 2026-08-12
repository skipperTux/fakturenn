using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
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

    private DataProtectionDbContext CreateDataProtectionContext() =>
        new(new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);
}
