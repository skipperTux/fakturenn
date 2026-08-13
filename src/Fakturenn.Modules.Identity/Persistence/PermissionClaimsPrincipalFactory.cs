using System.Security.Claims;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Stamps the permissions a user's roles grant into their principal, as claims of
/// type <see cref="PermissionClaims.Type"/>.
/// <para>
/// Without this, <c>PermissionAuthorizationHandler</c> reads a claim nothing ever
/// writes and every authorized endpoint returns 403 — including the administrator's
/// own. That is not hypothetical: it is what this plan specified until a spec review
/// caught it. Registering it is the load-bearing half; a unit test over this class
/// passes whether or not <c>AddClaimsPrincipalFactory</c> names it, which is why
/// <c>IdentityConfigurationTests</c> asserts the registration itself.
/// </para>
/// <para>
/// Claims are a cached authorization decision. Identity re-runs this factory at each
/// security-stamp validation, so the staleness window after a role change is bounded
/// by <c>SecurityStampValidatorOptions.ValidationInterval</c>, which the host sets to
/// one minute. The alternative — a database lookup per request — was rejected in the
/// spec for a staleness window the stamp interval already bounds.
/// </para>
/// <para>
/// Lives under <c>Persistence</c> rather than <c>Authorization</c>: it needs
/// <see cref="IdentityDbContext"/>, so it is the read-side adapter that turns stored
/// roles into claims. <c>Authorization</c> stays the pure policy vocabulary, and the
/// dependency between the two namespaces runs one way only.
/// </para>
/// </summary>
public sealed class PermissionClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> options,
    IdentityDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);

        List<string> permissions = await db.UserRoles
            .Where(userRole => userRole.UserId == user.Id)
            .Join(
                db.RolePermissions,
                userRole => userRole.RoleId,
                rolePermission => rolePermission.RoleId,
                (_, rolePermission) => rolePermission.Permission)
            .Distinct()
            .ToListAsync();

        foreach (string permission in permissions)
        {
            identity.AddClaim(new Claim(PermissionClaims.Type, permission));
        }

        return identity;
    }
}
