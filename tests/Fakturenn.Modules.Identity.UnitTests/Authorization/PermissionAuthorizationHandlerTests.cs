using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Fakturenn.Modules.Identity.UnitTests.Authorization;

public sealed class PermissionAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext ContextFor(string requiredPermission, params string[] granted)
    {
        var identity = new ClaimsIdentity(
            [.. granted.Select(p => new Claim(PermissionClaims.Type, p))],
            authenticationType: "Test");

        return new AuthorizationHandlerContext(
            [new PermissionRequirement(requiredPermission)],
            new ClaimsPrincipal(identity),
            resource: null);
    }

    [Fact]
    public async Task A_principal_holding_the_permission_succeeds()
    {
        AuthorizationHandlerContext context = ContextFor(Permissions.UsersManage, Permissions.UsersManage);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_principal_holding_a_different_permission_does_not_succeed()
    {
        AuthorizationHandlerContext context = ContextFor(Permissions.UsersManage, Permissions.UsersRead);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task An_unauthenticated_principal_does_not_succeed()
    {
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permissions.UsersManage)],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
