using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fakturenn.IntegrationTests;

[Collection(RealHost.Name)]
public sealed class CircuitRevalidationTests(SetupHostFixture host)
{
    // public Methods
    [Fact]
    public async Task A_circuit_principal_stays_valid_while_the_stamp_is_unchanged()
    {
        (UserManager<ApplicationUser> users, IdentityOptions options, ClaimsPrincipal principal, AsyncServiceScope scope) =
            await PrincipalAsync("revalidate-valid@example.test");
        await using (scope)
        {
            (await SecurityStampRevalidatingAuthenticationStateProvider.StampMatchesAsync(users, options, principal))
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_circuit_principal_is_invalidated_when_the_stamp_rotates()
    {
        // Locking, unlocking, password and MFA changes all rotate the stamp (E02a section
        // 8). An open circuit must notice within the same interval a cookie does.
        (UserManager<ApplicationUser> users, IdentityOptions options, ClaimsPrincipal principal, AsyncServiceScope scope) =
            await PrincipalAsync("revalidate-rotated@example.test");
        await using (scope)
        {
            ApplicationUser user = (await users.FindByEmailAsync("revalidate-rotated@example.test"))!;
            await users.UpdateSecurityStampAsync(user);

            (await SecurityStampRevalidatingAuthenticationStateProvider.StampMatchesAsync(users, options, principal))
                .Should().BeFalse();
        }
    }

    // private Methods
    private async Task<(UserManager<ApplicationUser>, IdentityOptions, ClaimsPrincipal, AsyncServiceScope)> PrincipalAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, TestContext.Current.CancellationToken);
        AsyncServiceScope scope = host.Services.CreateAsyncScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IUserClaimsPrincipalFactory<ApplicationUser> factory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        ClaimsPrincipal principal = await factory.CreateAsync((await users.FindByIdAsync(user.Id.ToString()))!);
        IdentityOptions options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        return (users, options, principal, scope);
    }
}
