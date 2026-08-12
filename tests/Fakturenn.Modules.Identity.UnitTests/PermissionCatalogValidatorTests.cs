using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;

namespace Fakturenn.Modules.Identity.UnitTests;

public sealed class PermissionCatalogValidatorTests
{
    [Fact]
    public void Stored_permissions_that_all_exist_in_code_produce_no_findings()
    {
        string[] stored = [Permissions.UsersRead, Permissions.UsersManage];

        PermissionCatalogValidator.FindUnknownPermissions(stored).Should().BeEmpty();
    }

    [Fact]
    public void A_stored_permission_the_code_does_not_define_is_reported()
    {
        // A stale or misspelt row grants nothing, which is indistinguishable from a
        // working configuration until someone is denied access they believe they have.
        string[] stored = [Permissions.UsersRead, "invoices.finalise"];

        PermissionCatalogValidator.FindUnknownPermissions(stored)
            .Should().ContainSingle().Which.Should().Be("invoices.finalise");
    }

    [Fact]
    public void Comparison_is_case_sensitive()
    {
        // PermissionAuthorizationHandler matches with StringComparison.Ordinal, so a
        // row reading "Users.Manage" grants nothing. The two mechanisms have to agree
        // about the same string, or one would report healthy while the other denies.
        PermissionCatalogValidator.FindUnknownPermissions(["Users.Manage"])
            .Should().ContainSingle();
    }

    [Fact]
    public void The_same_unknown_permission_stored_against_several_roles_is_reported_once()
    {
        // The finding is about the value, not about how many rows carry it. An
        // operator fixing it edits one string.
        PermissionCatalogValidator.FindUnknownPermissions(
                ["invoices.finalise", "invoices.finalise", Permissions.UsersRead])
            .Should().ContainSingle().Which.Should().Be("invoices.finalise");
    }
}
