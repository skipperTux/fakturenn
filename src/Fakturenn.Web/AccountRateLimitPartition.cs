using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Fakturenn.Web;

/// <summary>
/// The partition key for the <c>account</c> rate limiter: the best identity available for
/// this request, plus the client address.
/// <para>
/// The address is always part of the key so one compromised account cannot exhaust another
/// user's budget. The identity is always part of the key when there is one, because a key
/// of address alone is a self-DoS: the documented safe default is that no forwarded-header
/// trust is configured, so every client behind a reverse proxy or a NAT presents the same
/// address, and one shared budget would then cover every user of the instance. A five-person
/// office behind one address would lock itself out of its own second factor.
/// </para>
/// <para>
/// Measured, not assumed. An earlier version read only the <c>email</c> form field, which
/// only <c>/login/submit</c> and <c>/setup</c> carry — so the second-factor, change-password
/// and sign-out endpoints all fell back to the address alone, and the integration suite
/// exhausted that one partition and answered 429 to six unrelated tests.
/// </para>
/// </summary>
public static class AccountRateLimitPartition
{
    // public static Methods

    /// <summary>Builds the key. Never returns null; an unidentified caller keys on address alone.</summary>
    public static string KeyFor(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return $"{Identify(context)}|{address}";
    }

    // private static Methods

    /// <summary>
    /// The identity half of the key, in descending order of confidence. The first three
    /// branches all name an account; only the last is genuinely anonymous.
    /// </summary>
    private static string Identify(HttpContext context)
    {
        // Signed in. UseAuthentication runs before UseRateLimiter in the pipeline, so the
        // application cookie has already been turned into a principal by this point --
        // /change-password/submit and /logout land here.
        IdentityOptions identity = context.RequestServices
            .GetRequiredService<IOptions<IdentityOptions>>().Value;

        string? signedIn = context.User.FindFirstValue(identity.ClaimsIdentity.UserIdClaimType);
        if (!string.IsNullOrEmpty(signedIn))
        {
            return $"user:{signedIn}";
        }

        // Halfway through signing in: the password was accepted and the second factor has
        // not been supplied yet. UseAuthentication does not authenticate this scheme -- it
        // is not the default one -- so the ticket is read here instead.
        string? pending = TwoFactorUserId(context);
        if (!string.IsNullOrEmpty(pending))
        {
            return $"user:{pending}";
        }

        // An anonymous caller naming an account: /login/submit and /setup. Folded to upper
        // case rather than lower only because CA1308 rejects the other direction; a
        // partition key needs one consistent folding, not a particular one.
        string named = context.Request.HasFormContentType
            ? context.Request.Form["email"].ToString().Trim().ToUpperInvariant()
            : string.Empty;

        return named.Length == 0 ? string.Empty : $"name:{named}";
    }

    /// <summary>
    /// The user id inside Identity's two-factor cookie, or null when there is none.
    /// <para>
    /// The ticket is unprotected rather than the cookie's ciphertext being used as the key
    /// directly: a re-issued cookie is a different string for the same user, so keying on
    /// the ciphertext would let a caller reset their own budget by posting the password
    /// again. <see cref="ISecureDataFormat{TData}.Unprotect(string)"/> answers null for a
    /// value it cannot read, so a tampered or stale cookie falls through to the next
    /// branch rather than failing the request.
    /// </para>
    /// </summary>
    private static string? TwoFactorUserId(HttpContext context)
    {
        CookieAuthenticationOptions options = context.RequestServices
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.TwoFactorUserIdScheme);

        if (options.Cookie.Name is not { } name
            || !context.Request.Cookies.TryGetValue(name, out string? protectedTicket))
        {
            return null;
        }

        AuthenticationTicket? ticket = options.TicketDataFormat.Unprotect(protectedTicket);

        // ClaimTypes.Name, not the user-id claim type: SignInManager writes the id under
        // Name in this scheme and reads it back the same way. Verified against a live
        // ticket rather than taken from the documentation.
        return ticket?.Principal.FindFirstValue(ClaimTypes.Name);
    }
}
