using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// Constraints the Identity schema has to carry, asserted against real PostgreSQL
/// rather than the EF model, because a relationship configured in
/// <c>OnModelCreating</c> is only worth anything once it reaches the database.
/// <para>
/// No audit interceptor is wired here: these tests are about keys and foreign keys,
/// and the audit columns tolerate their defaults. <c>AuditStampingTests</c> covers
/// the stamping.
/// </para>
/// </summary>
public sealed class IdentitySchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Deleting_a_role_takes_its_permission_grants_with_it()
    {
        await using IdentityDbContext seeding = CreateContext();
        await seeding.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var role = new Role { Id = Guid.CreateVersion7(), Name = $"granting-{Guid.NewGuid():N}" };
        seeding.Roles.Add(role);

        // Two grants on one role. This is also what pins the composite primary key:
        // were it to collapse to RoleId alone, the second row would collide with the
        // first and a role could hold exactly one permission.
        seeding.RolePermissions.AddRange(
            new RolePermission { RoleId = role.Id, Permission = Permissions.UsersRead },
            new RolePermission { RoleId = role.Id, Permission = Permissions.UsersManage });
        await seeding.SaveChangesAsync(TestContext.Current.CancellationToken);

        int granted = await seeding.RolePermissions.AsNoTracking()
            .CountAsync(rp => rp.RoleId == role.Id, TestContext.Current.CancellationToken);
        granted.Should().Be(2);

        // A fresh context, so the grants are untracked and PostgreSQL's ON DELETE
        // CASCADE has to do the work rather than EF's client-side fixup.
        await using IdentityDbContext deleting = CreateContext();
        Role tracked = await deleting.Roles
            .SingleAsync(r => r.Id == role.Id, TestContext.Current.CancellationToken);
        deleting.Roles.Remove(tracked);
        await deleting.SaveChangesAsync(TestContext.Current.CancellationToken);

        bool anyRemain = await deleting.RolePermissions.AsNoTracking()
            .AnyAsync(rp => rp.RoleId == role.Id, TestContext.Current.CancellationToken);
        anyRemain.Should().BeFalse();
    }

    private IdentityDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);
}
