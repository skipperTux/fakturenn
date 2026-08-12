using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Fakturenn.Web;

/// <summary>
/// Confines a signed-in user with an outstanding obligation — TOTP enrolment, or a forced
/// password change — to the page that discharges it.
/// <para>
/// Runs after authentication so <c>HttpContext.User</c> is populated, and before endpoint
/// execution so no application page renders for a user who still owes one of these.
/// </para>
/// <para>
/// Without this, both flags are advisory. Sign-in redirects a user carrying
/// <c>MustChangePassword</c> to the change-password page, but a redirect is a suggestion:
/// typing any other URL walks past it. The gate is what makes the flags binding on every
/// request rather than on one response.
/// </para>
/// </summary>
public sealed class EnrolmentGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(users);

        // A performance guard, not the security one, and that was measured: deleting this
        // block leaves every test green, because GetUserAsync answers null for an anonymous
        // principal and the null branch below then declines to act. What it buys is one
        // avoided database round trip per anonymous request -- the login pages and their
        // posts, and every request before the first sign-in.
        //
        // The correctness property "anonymous callers are untouched" lives in that null
        // branch. Making a null user gated instead reddens half the integration suite,
        // including all of SignInTests and SetupEndpointTests: a gate that acts on anonymous
        // callers closes sign-in and first-run setup.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string path = context.Request.Path.Value ?? "/";
        if (EnrolmentGate.IsAllowedWhilePendingObligations(path))
        {
            await next(context);
            return;
        }

        ApplicationUser? user = await users.GetUserAsync(context.User);
        if (user is null)
        {
            // An authenticated principal whose user no longer exists — deleted mid-session.
            // Not this middleware's problem: the security-stamp validator ends that session
            // on its next revalidation. Redirecting here instead would send an
            // unidentifiable caller into an enrolment flow with no account behind it.
            await next(context);
            return;
        }

        // Enrolment first, password change second, and the order is deliberate. A user an
        // administrator has just created carries BOTH flags. Sending them to change their
        // password first would leave the account protected by one factor for the whole of
        // that exchange, and would hand them a new password before the second factor that
        // is supposed to back it exists. Enrolling first means the replacement password is
        // chosen by an account that already has two factors.
        if (user.MustEnrolTotp)
        {
            context.Response.Redirect("/account/enrol-totp");
            return;
        }

        if (user.MustChangePassword)
        {
            context.Response.Redirect("/account/change-password");
            return;
        }

        await next(context);
    }
}
