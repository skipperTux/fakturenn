using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

public sealed class RoleSeedingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Seeding_twice_leaves_exactly_one_administrator_role_with_every_permission()
    {
        await using IdentityDbContext context = postgres.CreateIdentityContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);
        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);

        int roleCount = await context.Roles
            .CountAsync(r => r.Name == RoleSeeder.AdministratorRoleName, TestContext.Current.CancellationToken);
        roleCount.Should().Be(1);

        Guid roleId = await ReadAdministratorRoleIdAsync(context);

        List<string> granted = await context.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync(TestContext.Current.CancellationToken);

        granted.Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task Seeding_restores_a_permission_the_role_has_lost()
    {
        // The property that distinguishes a re-sync from a create-if-absent, and the
        // one that matters when a later epic adds a permission constant: an existing
        // installation's Administrator role must gain it on the next --migrate.
        // PermissionCatalogValidator catches stored permissions the code does not
        // define; nothing but this catches permissions the code defines and the
        // database lacks.
        await using IdentityDbContext context = postgres.CreateIdentityContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);
        Guid roleId = await ReadAdministratorRoleIdAsync(context);

        RolePermission lost = await context.RolePermissions
            .SingleAsync(
                rp => rp.RoleId == roleId && rp.Permission == Permissions.UsersManage,
                TestContext.Current.CancellationToken);
        context.RolePermissions.Remove(lost);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        bool missing = await context.RolePermissions.AsNoTracking()
            .AnyAsync(
                rp => rp.RoleId == roleId && rp.Permission == Permissions.UsersManage,
                TestContext.Current.CancellationToken);
        missing.Should().BeFalse("the grant has to actually be gone for the re-grant below to mean anything");

        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);

        List<string> granted = await context.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync(TestContext.Current.CancellationToken);

        granted.Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task The_seeded_administrator_role_is_a_system_role()
    {
        // IsSystemRole is what stops the role being deleted or stripped through the
        // user interface, so an instance cannot be locked out of its own
        // administration. Seeding it false would leave that guard with nothing to hold.
        await using IdentityDbContext context = postgres.CreateIdentityContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);

        Role administrator = await context.Roles.AsNoTracking()
            .SingleAsync(
                r => r.Name == RoleSeeder.AdministratorRoleName,
                TestContext.Current.CancellationToken);

        administrator.IsSystemRole.Should().BeTrue();
    }

    [Fact]
    public async Task An_operator_created_role_is_left_untouched()
    {
        // Seeding re-syncs system roles only. A role an operator made is data, and a
        // seeder that "corrected" it would silently undo deliberate configuration.
        await using IdentityDbContext context = postgres.CreateIdentityContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var operatorRole = new Role
        {
            Id = Guid.CreateVersion7(),
            Name = $"reader-{Guid.NewGuid():N}",
            IsSystemRole = false,
        };
        context.Roles.Add(operatorRole);
        context.RolePermissions.Add(new RolePermission
        {
            RoleId = operatorRole.Id,
            Permission = Permissions.UsersRead,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);

        List<string> granted = await context.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == operatorRole.Id)
            .Select(rp => rp.Permission)
            .ToListAsync(TestContext.Current.CancellationToken);

        granted.Should().ContainSingle().Which.Should().Be(Permissions.UsersRead);
    }

    [Fact]
    public async Task The_catalogue_validator_reports_a_stored_permission_the_code_does_not_define()
    {
        // The same query shape the --migrate entrypoint runs, so the validator is
        // measured against what the database actually holds rather than a literal.
        await using IdentityDbContext context = postgres.CreateIdentityContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await RoleSeeder.SeedAsync(context, TestContext.Current.CancellationToken);
        Guid roleId = await ReadAdministratorRoleIdAsync(context);

        var stale = new RolePermission { RoleId = roleId, Permission = "invoices.finalise" };
        context.RolePermissions.Add(stale);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<string> stored = await context.RolePermissions.AsNoTracking()
            .Select(rolePermission => rolePermission.Permission)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);

        // Removed before asserting: the other tests in this class share the database
        // and assert the Administrator role's exact grants.
        context.RolePermissions.Remove(stale);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        PermissionCatalogValidator.FindUnknownPermissions(stored)
            .Should().ContainSingle().Which.Should().Be("invoices.finalise");
    }

    private static async Task<Guid> ReadAdministratorRoleIdAsync(IdentityDbContext context) =>
        await context.Roles.AsNoTracking()
            .Where(r => r.Name == RoleSeeder.AdministratorRoleName)
            .Select(r => r.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
}
