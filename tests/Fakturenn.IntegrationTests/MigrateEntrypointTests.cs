using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// Covers the <c>--migrate</c> entrypoint as a real process.
/// <para>
/// Seeding and permission-catalogue validation are wired in <c>Program.cs</c>'s
/// top-level statements, which nothing in-process can reach. A test over
/// <see cref="RoleSeeder"/> or <see cref="PermissionCatalogValidator"/> alone proves
/// the classes work and says nothing about whether the entrypoint calls them — which
/// is exactly the failure shape this epic keeps hitting.
/// </para>
/// </summary>
public sealed class MigrateEntrypointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string UndefinedPermission = "invoices.finalise";

    [Fact]
    public async Task Migrating_seeds_the_administrator_role_and_reports_success()
    {
        (int exitCode, string output) = await RunMigrateAsync();

        exitCode.Should().Be(0, output);
        output.Should().Contain("Seeded system roles.");

        await using IdentityDbContext context = postgres.CreateIdentityContext();
        Guid roleId = ReadAdministratorRoleId(context);

        List<string> granted = await context.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync(TestContext.Current.CancellationToken);

        granted.Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task A_stored_permission_this_version_does_not_define_fails_the_migration()
    {
        (int seedExitCode, string seedOutput) = await RunMigrateAsync();
        seedExitCode.Should().Be(0, seedOutput);

        await using IdentityDbContext context = postgres.CreateIdentityContext();
        Guid roleId = ReadAdministratorRoleId(context);

        var stale = new RolePermission { RoleId = roleId, Permission = UndefinedPermission };
        context.RolePermissions.Add(stale);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        try
        {
            (int exitCode, string output) = await RunMigrateAsync();

            // A deployment carrying a grant nothing enforces is blocked before it can
            // serve traffic, rather than silently denying access later.
            exitCode.Should().NotBe(0, output);
            output.Should().Contain(UndefinedPermission);
        }
        finally
        {
            // The fixture's database is shared with the other test in this class.
            context.RolePermissions.Remove(stale);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private static Guid ReadAdministratorRoleId(IdentityDbContext context) =>
        context.Roles.AsNoTracking()
            .Where(r => r.Name == RoleSeeder.AdministratorRoleName)
            .Select(r => r.Id)
            .Single();

    private Task<(int ExitCode, string Output)> RunMigrateAsync() =>
        HostProcess.RunAsync(
            postgres.ConnectionString,
            ["--migrate"],
            standardInput: null,
            TestContext.Current.CancellationToken);
}
