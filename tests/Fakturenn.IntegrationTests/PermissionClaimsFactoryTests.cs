using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// The claim <c>PermissionAuthorizationHandler</c> reads has to be written by
/// something. This is that something, exercised through the registration path rather
/// than by instantiating the factory.
/// </summary>
public sealed class PermissionClaimsFactoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_user_holding_the_administrator_role_receives_every_permission_as_a_claim()
    {
        await using IdentityDbContext db = postgres.CreateIdentityContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await RoleSeeder.SeedAsync(db, TestContext.Current.CancellationToken);

        ApplicationUser user = await postgres.CreateUserAsync("claims@example.test");
        Guid roleId = await db.Roles
            .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
            .Select(role => role.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        ClaimsPrincipal principal = await postgres.CreatePrincipalAsync(user);

        principal.Claims
            .Where(claim => claim.Type == PermissionClaims.Type)
            .Select(claim => claim.Value)
            .Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task A_user_holding_no_role_receives_no_permission_claims()
    {
        await using IdentityDbContext db = postgres.CreateIdentityContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        ApplicationUser user = await postgres.CreateUserAsync("noroles@example.test");

        ClaimsPrincipal principal = await postgres.CreatePrincipalAsync(user);

        principal.Claims.Should().NotContain(claim => claim.Type == PermissionClaims.Type);
    }
}
