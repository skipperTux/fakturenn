using System.Security.Cryptography;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fakturenn.Web.Components.Account;

public static class AccountEndpoints
{
    /// <summary>
    /// The PostgreSQL advisory-lock key that serialises first-run setup.
    /// <para>
    /// The value is the ASCII bytes of <c>FKTNSETU</c> ("Fakturenn setup") read as a
    /// big-endian 64-bit integer. It is derived from text rather than picked at random
    /// so it is reproducible and self-documenting, and it stays inside a positive
    /// <c>bigint</c>.
    /// </para>
    /// <para>
    /// Advisory locks share one namespace per database, so this key must be unique
    /// within the database and <b>must never change</b>: a different key is a different
    /// lock, and two application versions taking different keys would not exclude each
    /// other during a rolling deployment. Any later operator entrypoint that creates the
    /// first administrator has to take <i>this</i> key.
    /// </para>
    /// </summary>
    private const long SetupLockKey = 0x464B544E53455455L;

    /// <summary>
    /// Carries the freshly generated recovery codes from the enrolment post to the page
    /// that displays them.
    /// <para>
    /// A cookie rather than <c>TempData</c> or session state, because neither exists in
    /// this application and adding server-side session state for one redirect would be a
    /// dependency the rest of the design does not need. The value is data-protected, so
    /// what crosses the wire and sits in the browser's cookie jar is ciphertext.
    /// </para>
    /// </summary>
    private const string RecoveryCookieName = "fakturenn_recovery";

    /// <summary>
    /// The purpose string is part of the key derivation. Changing it makes an
    /// already-issued cookie undecryptable, which for this cookie means a user loses the
    /// only display of their recovery codes. Do not edit it.
    /// </summary>
    private const string RecoveryProtectorPurpose = "Fakturenn.RecoveryCodeDisplay.v1";

    /// <summary>Ten, per the spec. Shown once, each usable once.</summary>
    private const int RecoveryCodeCount = 10;

    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/account").RequireRateLimiting("account");

        group.MapPost("/setup", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            IdentityDbContext db,
            CancellationToken cancellationToken) =>
        {
            // Read outside the transaction: parsing the request body is not database work
            // and must not hold the advisory lock below.
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string email = form["email"].ToString().Trim();
            string displayName = form["displayName"].ToString().Trim();
            string password = form["password"].ToString();

            // An explicit transaction under a DbContext configured with
            // EnableRetryOnFailure (see IdentityConfiguration) must go through the
            // execution strategy, or EF throws InvalidOperationException. Disabling the
            // retry to avoid this wrapper is not an option -- the retry is deliberate.
            IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(
                async token =>
                {
                    await using IDbContextTransaction transaction =
                        await db.Database.BeginTransactionAsync(token);

                    // THE mechanism. The count check and the insert are not atomic, so
                    // without this every concurrent post passes the check and every one
                    // of them becomes an administrator -- measured, four out of four.
                    //
                    // A unique index does NOT serialise this: it only rejects rows that
                    // collide on the indexed value, so it stops two posts using the SAME
                    // e-mail address and does nothing about two posts using different
                    // ones, which is the case that matters. That reasoning was in this
                    // comment before it was tested, and it was wrong.
                    //
                    // pg_advisory_xact_lock is transaction-scoped: it releases on commit
                    // or rollback, so there is no cleanup path to forget. It records no
                    // state either, which is why a marker row was rejected -- restore a
                    // partial backup and a marker says "configured" while zero users
                    // exist, bricking the instance. Zero users must reopen /setup.
                    await db.Database.ExecuteSqlAsync(
                        $"SELECT pg_advisory_xact_lock({SetupLockKey})", token);

                    // Re-checked server-side. The page's own guard is a redirect for
                    // humans; this is the one that actually closes the endpoint, and
                    // under the lock it is now a genuine check-and-act.
                    if (await db.Users.AnyAsync(token))
                    {
                        return Results.NotFound();
                    }

                    var user = new ApplicationUser
                    {
                        Id = Guid.CreateVersion7(),
                        UserName = email,
                        Email = email,
                        DisplayName = displayName,
                        CreatedAt = DateTimeOffset.UtcNow,
                        MustEnrolTotp = true,
                    };

                    IdentityResult created;
                    try
                    {
                        // Password hashing happens inside the lock. That serialises
                        // concurrent first-run posts by roughly one hash each, which is
                        // acceptable on an endpoint that succeeds exactly once per
                        // installation and sits behind the "account" rate limiter.
                        created = await users.CreateAsync(user, password);
                    }
                    catch (DbUpdateException)
                    {
                        // Belt and braces, not the mechanism. The advisory lock excludes
                        // anything that takes the same key; this still catches a writer
                        // that does not -- an operator entrypoint creating an
                        // administrator from another connection, say -- but only when the
                        // user names collide. Task 14 must take SetupLockKey rather than
                        // rely on this.
                        return Results.Redirect("/account/login");
                    }

                    if (!created.Succeeded)
                    {
                        // Identity reports a duplicate user name as a validation failure
                        // rather than an exception, so the same unguarded writer can
                        // surface either way depending on how the store is configured.
                        if (created.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
                        {
                            return Results.Redirect("/account/login");
                        }

                        string message = string.Join(" ", created.Errors.Select(e => e.Description));
                        return Results.Redirect($"/setup?error={Uri.EscapeDataString(message)}");
                    }

                    await RoleSeeder.SeedAsync(db, token);

                    Guid administratorRoleId = await db.Roles
                        .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
                        .Select(role => role.Id)
                        .SingleAsync(token);

                    db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = administratorRoleId });
                    await db.SaveChangesAsync(token);

                    await transaction.CommitAsync(token);

                    return Results.Redirect("/account/login");
                },
                cancellationToken);
        });

        // "/enrol-totp/verify", not "/enrol-totp". A Blazor static-SSR page endpoint
        // accepts POST as well as GET, so mapping this handler on the page's own route
        // produces two candidates with identical precedence and every post fails with
        // AmbiguousMatchException. The same reason the setup page at "/setup" posts to
        // "/account/setup": in this application a form's action is never its page route.
        group.MapPost("/enrol-totp/verify", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn,
            CancellationToken cancellationToken) =>
        {
            ApplicationUser? user = await users.GetUserAsync(http.User);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);

            // Authenticator apps group the digits; a user copying from one brings the
            // grouping with them.
            string code = form["code"].ToString().Replace(" ", string.Empty, StringComparison.Ordinal);

            bool valid = await users.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, code);

            if (!valid)
            {
                // Nothing is written on a failure. MustEnrolTotp in particular stays set:
                // it is the flag the enrolment gate reads, so clearing it on anything
                // short of a verified code would make the gate decorative.
                return Results.Redirect("/account/enrol-totp?error=invalid");
            }

            await users.SetTwoFactorEnabledAsync(user, true);
            user.MustEnrolTotp = false;
            await users.UpdateAsync(user);

            // SetTwoFactorEnabledAsync rotates the security stamp, and the validation
            // interval is one minute (IdentityConfiguration), so without this the user is
            // signed out roughly a minute after finishing enrolment -- the cookie still
            // carries the stamp it was issued under, and the validator ends the session
            // once it no longer matches. Re-issuing the cookie under the new stamp is the
            // whole fix; the rotation itself is wanted.
            await signIn.RefreshSignInAsync(user);

            IEnumerable<string>? codes =
                await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

            StashRecoveryCodes(http, codes ?? []);

            return Results.Redirect("/account/recovery-codes");
        });

        // Every route below is the page route plus "/submit", for the reason given above
        // the enrolment handler: the Blazor page at the same path already answers POST, so
        // mapping a handler on the page's own route makes every post an
        // AmbiguousMatchException at request time.
        group.MapPost("/login/submit", async (
            HttpContext http,
            SignInManager<ApplicationUser> signIn,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string email = form["email"].ToString().Trim();
            string password = form["password"].ToString();

            // lockoutOnFailure: true is what makes the failure counter durable. Lockout is
            // a database column, so it is the control that survives a restart; the rate
            // limiter in front of this endpoint only blunts the enumeration oracle that
            // lockout on its own would create.
            SignInResult result = await signIn.PasswordSignInAsync(
                email, password, isPersistent: false, lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                return Results.Redirect("/account/lockout");
            }

            // Not a session. PasswordSignInAsync issues the two-factor cookie only, and the
            // application cookie is issued by the handler below once the second factor is
            // supplied. A user with TwoFactorEnabled set can never reach an authorised page
            // on a password alone.
            if (result.RequiresTwoFactor)
            {
                return Results.Redirect("/account/login-2fa");
            }

            if (!result.Succeeded)
            {
                // One answer for an unknown account and for a wrong password: identical
                // status, identical location, identical body. Anything that distinguished
                // them would let an attacker enumerate valid addresses without ever
                // guessing a password.
                return Results.Redirect("/account/login?error=invalid");
            }

            // Reached only by a user who has not enrolled a second factor yet. They are
            // signed in so they can reach the enrolment page; the enrolment gate is what
            // confines them to it.
            return Results.Redirect("/");
        });

        group.MapPost("/login-2fa/submit", async (
            HttpContext http,
            SignInManager<ApplicationUser> signIn,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);

            // Authenticator apps group the digits; a user copying from one brings the
            // grouping with them.
            string code = form["code"].ToString().Replace(" ", string.Empty, StringComparison.Ordinal);

            SignInResult result = await signIn.TwoFactorAuthenticatorSignInAsync(
                code, isPersistent: false, rememberClient: false);

            if (result.IsLockedOut)
            {
                return Results.Redirect("/account/lockout");
            }

            if (!result.Succeeded)
            {
                return Results.Redirect("/account/login-2fa?error=invalid");
            }

            // Somebody else chose this password -- an administrator creating the account,
            // or an operator running --reset-password. Send them to change it before
            // anything else, so a shared credential stops being shared the first time it
            // is used.
            //
            // Reading http.User AFTER the sign-in works, and that is not obvious:
            // Response.Cookies is where the ticket goes, so the request principal would
            // normally still be anonymous here. SignInManager.SignInWithClaimsAsync also
            // assigns HttpContext.User, which is what makes this line find the user.
            // Measured rather than assumed -- reading the two-factor user before the
            // sign-in instead leaves every test green, so both forms work and this one is
            // the plan's. Do not "fix" it without re-measuring.
            ApplicationUser? pending = await signIn.UserManager.GetUserAsync(http.User);
            return pending?.MustChangePassword == true
                ? Results.Redirect("/account/change-password")
                : Results.Redirect("/");
        });

        group.MapPost("/login-recovery/submit", async (
            HttpContext http,
            SignInManager<ApplicationUser> signIn,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string code = form["code"].ToString().Replace(" ", string.Empty, StringComparison.Ordinal);

            // TwoFactorRecoveryCodeSignInAsync redeems the code as part of accepting it, so
            // a code is spent whether or not the user goes on to use the session. Verifying
            // without redeeming would turn a one-shot credential into a second password.
            SignInResult result = await signIn.TwoFactorRecoveryCodeSignInAsync(code);

            return result.Succeeded
                ? Results.Redirect("/")
                : Results.Redirect("/account/login-recovery?error=invalid");
        });

        group.MapPost("/change-password/submit", async (
            HttpContext http,
            UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn,
            CancellationToken cancellationToken) =>
        {
            ApplicationUser? user = await users.GetUserAsync(http.User);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            IFormCollection form = await http.Request.ReadFormAsync(cancellationToken);
            string current = form["currentPassword"].ToString();
            string replacement = form["newPassword"].ToString();

            IdentityResult changed = await users.ChangePasswordAsync(user, current, replacement);
            if (!changed.Succeeded)
            {
                // MustChangePassword is deliberately untouched here. Clearing it on
                // anything short of a successful change would let a user walk past the
                // forced change by submitting a rejected one.
                string message = string.Join(" ", changed.Errors.Select(e => e.Description));
                return Results.Redirect($"/account/change-password?error={Uri.EscapeDataString(message)}");
            }

            user.MustChangePassword = false;
            await users.UpdateAsync(user);

            // ChangePasswordAsync rotates the security stamp, which invalidates every
            // session including this one. Re-sign-in so the user is not bounced to the
            // login page immediately after succeeding.
            await signIn.RefreshSignInAsync(user);

            return Results.Redirect("/");
        });

        // No page at this route, so no "/submit" suffix is needed -- and none is wanted
        // either, because the sign-out form posts here from the layout on every page.
        group.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();

            return Results.Redirect("/account/login");
        });
    }

    /// <summary>
    /// Reads the stashed recovery codes and clears the cookie, so they are displayed
    /// exactly once.
    /// <para>
    /// The deletion happens <b>before</b> the unprotect, deliberately: a cookie that
    /// cannot be unprotected -- tampered with, or encrypted under a key ring this instance
    /// no longer has -- would otherwise survive and fail identically on every subsequent
    /// request. Clearing first means the failure is self-healing.
    /// </para>
    /// </summary>
    internal static string[] TakeRecoveryCodes(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (!http.Request.Cookies.TryGetValue(RecoveryCookieName, out string? protectedValue))
        {
            return [];
        }

        http.Response.Cookies.Delete(RecoveryCookieName);

        try
        {
            return CreateRecoveryProtector(http)
                .Unprotect(protectedValue)
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (CryptographicException)
        {
            // An empty display, not a 500. The codes are already on the account; the page
            // says so, and the recovery path is an administrator forcing re-enrolment.
            return [];
        }
    }

    private static void StashRecoveryCodes(HttpContext http, IEnumerable<string> codes)
    {
        http.Response.Cookies.Append(
            RecoveryCookieName,
            CreateRecoveryProtector(http).Protect(string.Join(';', codes)),
            new CookieOptions
            {
                HttpOnly = true,

                // Strict rather than Lax: nothing links into this page from elsewhere, so
                // there is no cross-site navigation to accommodate.
                SameSite = SameSiteMode.Strict,

                // The equivalent of the authentication cookie's SameAsRequest policy,
                // written out because CookieOptions has no SecurePolicy -- that lives on
                // CookieBuilder, which configures a scheme rather than one Append call.
                // Marking it Secure unconditionally would silently drop the cookie on the
                // reference Compose deployment, which serves plain HTTP on localhost.
                Secure = http.Request.IsHttps,

                // Long enough to survive the redirect and a slow page load, short enough
                // that an abandoned enrolment does not leave the codes retrievable.
                MaxAge = TimeSpan.FromMinutes(5),
            });
    }

    private static IDataProtector CreateRecoveryProtector(HttpContext http) =>
        http.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(RecoveryProtectorPurpose);
}
