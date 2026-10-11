using System.Globalization;
using AwesomeAssertions;
using Fakturenn.Infrastructure.DataProtection;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Modules.Invoices.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

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

    [Fact]
    public async Task Migrate_provisions_the_messaging_schema()
    {
        // Wolverine's tables are created here and nowhere else: the host deliberately
        // omits UseResourceSetupOnStartup, so an application that has never been
        // migrated has no message storage at all.
        (int exitCode, string output) = await RunMigrateAsync();
        exitCode.Should().Be(0, output);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'messaging'";
        object? found = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Convert.ToInt64(found, CultureInfo.InvariantCulture).Should().Be(
            1,
            "--migrate is the only thing that may create Wolverine's schema");
    }

    [Fact]
    public async Task Migrate_upgrades_a_database_at_the_previous_release()
    {
        // Definition of Done: "migrations work from clean and previous states" (review C11).
        // The previous state is main before Stage 1: Data Protection and Identity migrated,
        // Invoices at its InitialCreate, the four new schemas absent. The class fixture's
        // database is shared with tests that migrate everything, so this one works in its own.
        NpgsqlConnectionStringBuilder builder = new(postgres.ConnectionString) { Database = "previous_release" };
        await using (NpgsqlConnection administration = new(postgres.ConnectionString))
        {
            await administration.OpenAsync(TestContext.Current.CancellationToken);
            await using NpgsqlCommand drop = administration.CreateCommand();
            drop.CommandText = "DROP DATABASE IF EXISTS previous_release WITH (FORCE)";
            await drop.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using NpgsqlCommand create = administration.CreateCommand();
            create.CommandText = "CREATE DATABASE previous_release";
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        string connectionString = builder.ConnectionString;
        await using (DataProtectionDbContext dataProtection = new(
            new DbContextOptionsBuilder<DataProtectionDbContext>().UseNpgsql(connectionString).Options))
        {
            await dataProtection.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using (IdentityDbContext identity = new(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options,
            DataProtectionProvider.Create("Fakturenn.Tests")))
        {
            await identity.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using (InvoicesDbContext invoices = new(
            new DbContextOptionsBuilder<InvoicesDbContext>().UseNpgsql(connectionString).Options))
        {
            await invoices.GetService<IMigrator>().MigrateAsync("20260807133735_InitialCreate", cancellationToken: TestContext.Current.CancellationToken);
        }

        (int exitCode, string output) = await HostProcess.RunAsync(
            connectionString, ["--migrate"], standardInput: null, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, output);
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand schemas = connection.CreateCommand();
        schemas.CommandText =
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name IN "
            + "('organizations', 'customers', 'projects', 'catalog', 'invoices', 'messaging')";
        Convert.ToInt64(await schemas.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture)
            .Should().Be(6);
        await using NpgsqlCommand organizations = connection.CreateCommand();
        organizations.CommandText = "SELECT count(*) FROM organizations.\"Organizations\"";
        Convert.ToInt64(await organizations.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture)
            .Should().Be(1);
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
