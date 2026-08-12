using AwesomeAssertions;
using Fakturenn.Modules.Identity.Persistence;

namespace Fakturenn.Modules.Identity.UnitTests;

public sealed class AdministratorGuardTests
{
    [Fact]
    public void Removing_the_only_administrator_is_refused()
    {
        AdministratorGuard.WouldRemoveLastAdministrator(administratorCount: 1, targetIsAdministrator: true)
            .Should().BeTrue();
    }

    [Fact]
    public void Removing_one_of_several_administrators_is_allowed()
    {
        AdministratorGuard.WouldRemoveLastAdministrator(administratorCount: 2, targetIsAdministrator: true)
            .Should().BeFalse();
    }

    [Fact]
    public void Removing_a_user_who_is_not_an_administrator_is_allowed()
    {
        AdministratorGuard.WouldRemoveLastAdministrator(administratorCount: 1, targetIsAdministrator: false)
            .Should().BeFalse();
    }

    [Fact]
    public void A_count_that_has_already_reached_zero_still_refuses()
    {
        // Defensive: a caller that counted after the removal, or raced with another
        // administrator being deleted, must not be told the removal is safe.
        AdministratorGuard.WouldRemoveLastAdministrator(administratorCount: 0, targetIsAdministrator: true)
            .Should().BeTrue();
    }
}
