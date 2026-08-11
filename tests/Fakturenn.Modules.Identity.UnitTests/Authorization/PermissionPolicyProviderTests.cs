using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Fakturenn.Modules.Identity.UnitTests.Authorization;

public sealed class PermissionPolicyProviderTests
{
    private static PermissionPolicyProvider CreateProvider() =>
        new(Options.Create(new AuthorizationOptions()));

    [Fact]
    public async Task A_known_permission_name_yields_a_policy_requiring_it()
    {
        AuthorizationPolicy? policy = await CreateProvider().GetPolicyAsync(Permissions.UsersManage);

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle()
            .Which.Should().BeOfType<PermissionRequirement>()
            .Which.Permission.Should().Be(Permissions.UsersManage);
    }

    [Fact]
    public async Task A_name_that_is_not_a_defined_permission_yields_no_policy()
    {
        // Guards against a typo in an [Authorize(Policy = "...")] silently
        // becoming an allow-all instead of a build or request failure.
        AuthorizationPolicy? policy = await CreateProvider().GetPolicyAsync("users.manag");

        policy.Should().BeNull();
    }

    [Fact]
    public async Task A_name_that_is_not_a_permission_is_resolved_by_the_fallback_provider()
    {
        // Distinguishes "delegates to the fallback" from "returns null". With an empty
        // AuthorizationOptions both look identical, which is why the existing test
        // passes even against a hardcoded null.
        var options = new AuthorizationOptions();
        options.AddPolicy("legacy.policy", policy => policy.RequireAssertion(_ => true));

        AuthorizationPolicy? policy =
            await new PermissionPolicyProvider(Options.Create(options)).GetPolicyAsync("legacy.policy");

        policy.Should().NotBeNull("a name that is not a permission must still reach the fallback provider");
    }

    [Fact]
    public void Every_declared_constant_is_present_in_the_catalogue()
    {
        // Two permissions, both with a named enforcement site. roles.read and
        // roles.manage were removed by the spec review: a permission constant with
        // nothing enforcing it is speculative surface, and E02b adds them together
        // with the role-management UI that will enforce them.
        Permissions.All.Should().BeEquivalentTo(
        [
            Permissions.UsersRead,
            Permissions.UsersManage,
        ]);
    }
}
