using System.Security.Claims;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// A real PostgreSQL instance per test class. SPEC-v0.1.md section 10 requires
/// real infrastructure through Testcontainers rather than an in-memory provider,
/// because schemas, sequences and concurrency behaviour are the point.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("fakturenn")
        .WithUsername("fakturenn")
        .WithPassword("fakturenn")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public InvoicesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public IdentityDbContext CreateIdentityContext() =>
        new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(ConnectionString)
                .Options,
            DataProtectionProvider.Create("Fakturenn.Tests"));

    /// <summary>
    /// Creates a user through the real <see cref="UserManager{TUser}"/>, so the
    /// security stamp and normalised names are set the way sign-in expects.
    /// </summary>
    public async Task<ApplicationUser> CreateUserAsync(string email)
    {
        await using ServiceProvider provider = BuildIdentityServices();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        UserManager<ApplicationUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = email,
        };

        IdentityResult result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the test user: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        return user;
    }

    /// <summary>
    /// Builds a principal the way the host does — through
    /// <see cref="IUserClaimsPrincipalFactory{TUser}"/> resolved from a container that
    /// registers the factory with <c>AddClaimsPrincipalFactory</c>, not by calling
    /// <see cref="PermissionClaimsPrincipalFactory"/> directly. Calling it directly
    /// would prove the class works and say nothing about whether anything uses it.
    /// </summary>
    public async Task<ClaimsPrincipal> CreatePrincipalAsync(ApplicationUser user)
    {
        await using ServiceProvider provider = BuildIdentityServices();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        IUserClaimsPrincipalFactory<ApplicationUser> factory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();

        return await factory.CreateAsync(user);
    }

    private ServiceProvider BuildIdentityServices()
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(
            DataProtectionProvider.Create("Fakturenn.Tests"));
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(ConnectionString));
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddClaimsPrincipalFactory<PermissionClaimsPrincipalFactory>();

        return services.BuildServiceProvider();
    }
}
