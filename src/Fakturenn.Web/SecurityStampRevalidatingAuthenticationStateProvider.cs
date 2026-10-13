using System.Security.Claims;
using Fakturenn.Modules.Identity.Domain;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Fakturenn.Web;

/// <summary>
/// Re-checks an open circuit's security stamp. Components.Server sets a circuit's user only
/// when it starts and when it reconnects (decompiled, 10.0.12); without this a user locked
/// mid-session keeps editing the IBAN and finalizing until the tab closes (review S3).
/// When the stamp no longer matches, the base class turns the state anonymous and
/// <see cref="CircuitOperations"/> refuses the next operation.
/// </summary>
public sealed class SecurityStampRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopes,
    IOptions<IdentityOptions> identityOptions,
    IOptions<SecurityStampValidatorOptions> stampOptions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    // protected Properties
    // The interval cookies already use, so a circuit and a page load agree on how stale a
    // revoked session may be.
    protected override TimeSpan RevalidationInterval => stampOptions.Value.ValidationInterval;

    // public static Methods
    public static async Task<bool> StampMatchesAsync(
        UserManager<ApplicationUser> users,
        IdentityOptions options,
        ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(options);

        ApplicationUser? user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }

        string? principalStamp = principal.FindFirstValue(options.ClaimsIdentity.SecurityStampClaimType);
        string userStamp = await users.GetSecurityStampAsync(user);
        return string.Equals(principalStamp, userStamp, StringComparison.Ordinal);
    }

    // protected Methods
    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await StampMatchesAsync(users, identityOptions.Value, authenticationState.User);
    }
}
