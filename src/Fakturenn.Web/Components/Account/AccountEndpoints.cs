using System.Security.Cryptography;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Fakturenn.Web.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Localization;

namespace Fakturenn.Web.Components.Account;

public static class AccountEndpoints
{
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

    /// <summary>
    /// The <c>?error=</c> value every credential form redirects with. A sentinel, not a
    /// message: <c>Login</c>, <c>LoginWith2fa</c>, <c>LoginWithRecoveryCode</c> and
    /// <c>EnrolTotp</c> each render their own localized sentence and none of them echoes this
    /// value, so nothing the user typed into a credential field can reach a page through it —
    /// and the sign-in page keeps answering identically whether the account exists or the
    /// password was wrong.
    /// </summary>
    private const string InvalidSubmission = "invalid";

    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // EVERY handler below takes the form as an IFormCollection PARAMETER, and none of
        // them calls http.Request.ReadFormAsync. That is the whole antiforgery story for
        // this file, so it is worth spelling out rather than leaving as a style.
        //
        // RequestDelegateFactory infers IAntiforgeryMetadata for an endpoint that BINDS a
        // form, and the generated delegate is also what turns a failed validation into a
        // 400 before the handler body runs. Read the form off HttpContext instead and both
        // halves are lost: no metadata, so UseAntiforgery skips the endpoint entirely --
        // measured in Task 9, where a token-less post to /account/setup answered 302 and
        // created the administrator while all seven forms had been rendering
        // <AntiforgeryToken /> since they were written.
        //
        // Endpoint metadata alone is NOT the fix, and that was measured too: with
        // RequireAntiforgeryTokenAttribute on this group and the handlers still reading the
        // form by hand, AntiforgeryMiddleware validates but does not reject -- it records
        // the outcome in IAntiforgeryValidationFeature and calls the next middleware, and
        // FormFeature.ReadFormAsync then throws InvalidOperationException. A forged post
        // answered 500 instead of 400. The parameter is what makes the framework refuse it.
        //
        // /account/logout binds a form it never reads for exactly this reason: it is the
        // one endpoint here with no fields, and without the parameter it would be the one
        // endpoint with no antiforgery.
        //
        // /account/setup is deliberately NOT exempted, which reverses Task 9's disposition
        // rather than repeating it. That reasoning was "an attacker who can reach an
        // unconfigured instance can simply POST it directly", and it holds only for an
        // attacker who can reach it. Cross-site request forgery is the case where they
        // cannot: an instance on a private network, reachable by a victim's browser and by
        // nothing the attacker controls, is claimed with a password of the attacker's
        // choosing by a page the victim merely visits. The setup form already renders a
        // token; validating it costs nothing and closes that.
        RouteGroupBuilder group = endpoints.MapGroup("/account").RequireRateLimiting("account");

        group.MapPost("/setup", async (
            HttpContext http,
            IFormCollection form,
            UserManager<ApplicationUser> users,
            IdentityDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            // Bound, so it is parsed before the handler body and outside the transaction:
            // parsing the request body is not database work and must not hold the advisory
            // lock below.
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
                    // See SetupLock for why the key is shared with --create-admin and
                    // why a unique index does not serialise this.
                    await SetupLock.TakeAsync(db, token);

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
                        // user names collide. --create-admin therefore takes SetupLock
                        // rather than relying on this.
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

                        // Back to /setup carrying the address and display name that were
                        // typed, so a password the policy refuses costs a correction rather
                        // than a retype. The password itself is not among the fields
                        // AccountForms carries -- see the allowlist there.
                        return AccountForms.Rejected(
                            http,
                            string.Join(" ", created.Errors.Select(e => e.Description)));
                    }

                    await RoleSeeder.SeedAsync(db, token);

                    Guid administratorRoleId = await db.Roles
                        .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
                        .Select(role => role.Id)
                        .SingleAsync(token);

                    db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = administratorRoleId });
                    await db.SaveChangesAsync(token);

                    await transaction.CommitAsync(token);

                    // After the commit, so the log never claims an administrator that a
                    // rolled-back transaction did not leave behind. This is the single most
                    // security-significant event the application emits: the one moment an
                    // unauthenticated caller mints administrative access.
                    AuthEventLog.Account(loggerFactory, AuthEvents.FirstAdministratorCreated, email);

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
            IFormCollection form,
            UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn,
            ILoggerFactory loggerFactory) =>
        {
            ApplicationUser? user = await users.GetUserAsync(http.User);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            // The same guard the enrolment page carries, for the same reason: this handler
            // ends in GenerateNewTwoFactorRecoveryCodesAsync, which REPLACES the stored set.
            // Without it, any authenticated session -- a stolen cookie included -- silently
            // invalidates the recovery codes their owner already wrote down, which is a
            // denial of the second factor rather than a use of it.
            //
            // MustEnrolTotp, and not TwoFactorEnabled, deliberately. See EnrolTotp.razor for
            // the argument: this predicate is the enrolment gate's own, and any other one
            // admits a state where the gate redirects a user to a page that refuses them.
            if (!user.MustEnrolTotp)
            {
                return Results.Redirect("/");
            }

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
                return AccountForms.Rejected(http, InvalidSubmission);
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

            AuthEventLog.Account(loggerFactory, AuthEvents.TotpEnrolled, user.Email!);

            IEnumerable<string>? codes =
                await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

            // The codes themselves never reach the log, only the fact that enrolment
            // finished. They are credential material with the same weight as a password.
            StashRecoveryCodes(http, codes ?? []);

            return Results.Redirect("/account/recovery-codes");
        });

        // Every route below is the page route plus "/submit", for the reason given above
        // the enrolment handler: the Blazor page at the same path already answers POST, so
        // mapping a handler on the page's own route makes every post an
        // AmbiguousMatchException at request time.
        group.MapPost("/login/submit", async (
            HttpContext http,
            IFormCollection form,
            SignInManager<ApplicationUser> signIn,
            ILoggerFactory loggerFactory) =>
        {
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
                AuthEventLog.AccountAlert(loggerFactory, AuthEvents.AccountLockedOut, email);

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
                //
                // The log keeps the same silence. The plan's {Reason} property is
                // deliberately absent: an operator needs to see that this address is being
                // attempted, not which half of the credential was wrong, and anyone who can
                // read the log would otherwise hold the enumeration oracle the endpoint
                // refuses to hand out.
                AuthEventLog.AccountAlert(loggerFactory, AuthEvents.SignInFailed, email);

                // The address comes back with the caller so they only retype the
                // password. It tells them nothing they did not just type, so it is not the
                // account oracle the identical message above exists to avoid.
                return AccountForms.Rejected(http, InvalidSubmission);
            }

            // Reached only by a user who has not enrolled a second factor yet. They are
            // signed in so they can reach the enrolment page; the enrolment gate is what
            // confines them to it.
            AuthEventLog.Account(loggerFactory, AuthEvents.SignInSucceeded, email);

            return Results.Redirect("/");
        });

        group.MapPost("/login-2fa/submit", async (
            HttpContext http,
            IFormCollection form,
            SignInManager<ApplicationUser> signIn,
            ILoggerFactory loggerFactory) =>
        {
            // Authenticator apps group the digits; a user copying from one brings the
            // grouping with them.
            string code = form["code"].ToString().Replace(" ", string.Empty, StringComparison.Ordinal);

            // Read BEFORE the exchange, not after: a successful sign-in deletes the
            // two-factor cookie, so afterwards there is no challenged user left to name and
            // every success would be logged against "unknown".
            string email = await ChallengedEmailAsync(signIn);

            SignInResult result = await signIn.TwoFactorAuthenticatorSignInAsync(
                code, isPersistent: false, rememberClient: false);

            if (result.IsLockedOut)
            {
                AuthEventLog.AccountAlert(loggerFactory, AuthEvents.AccountLockedOut, email);

                return Results.Redirect("/account/lockout");
            }

            if (!result.Succeeded)
            {
                // The code itself is never logged. A six-digit TOTP is short-lived but it is
                // still a credential, and a rejected one is often a mistyped valid one.
                AuthEventLog.AccountAlert(loggerFactory, AuthEvents.TwoFactorFailed, email);

                return AccountForms.Rejected(http, InvalidSubmission);
            }

            AuthEventLog.Account(loggerFactory, AuthEvents.TwoFactorSucceeded, email);

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
            IFormCollection form,
            SignInManager<ApplicationUser> signIn,
            ILoggerFactory loggerFactory) =>
        {
            string code = form["code"].ToString().Replace(" ", string.Empty, StringComparison.Ordinal);

            // Before the exchange, for the same reason as the authenticator handler above.
            string email = await ChallengedEmailAsync(signIn);

            // TwoFactorRecoveryCodeSignInAsync redeems the code as part of accepting it, so
            // a code is spent whether or not the user goes on to use the session. Verifying
            // without redeeming would turn a one-shot credential into a second password.
            SignInResult result = await signIn.TwoFactorRecoveryCodeSignInAsync(code);

            if (!result.Succeeded)
            {
                // Warning, alongside SignInFailed and TwoFactorFailed. Logging only the
                // successes here would record the outcomes that are usually innocent and drop
                // the ones that are not: ten single-use codes have no failure counter of their
                // own beyond the shared account lockout, so an attacker working through the
                // space is invisible without this line. Neither the code tried nor the number
                // of codes left is recorded.
                AuthEventLog.AccountAlert(loggerFactory, AuthEvents.RecoveryCodeFailed, email);

                return AccountForms.Rejected(http, InvalidSubmission);
            }

            // A warning, not information: a recovery code is the exceptional path, it is
            // spent by being accepted, and a user who reaches for one has usually lost their
            // authenticator -- or somebody else has found their codes. The code itself is
            // never logged.
            AuthEventLog.AccountAlert(loggerFactory, AuthEvents.RecoveryCodeUsed, email);

            return Results.Redirect("/");
        });

        group.MapPost("/change-password/submit", async (
            HttpContext http,
            IFormCollection form,
            UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn,
            ILoggerFactory loggerFactory) =>
        {
            ApplicationUser? user = await users.GetUserAsync(http.User);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            string current = form["currentPassword"].ToString();
            string replacement = form["newPassword"].ToString();

            IdentityResult changed = await users.ChangePasswordAsync(user, current, replacement);
            if (!changed.Succeeded)
            {
                // MustChangePassword is deliberately untouched here. Clearing it on
                // anything short of a successful change would let a user walk past the
                // forced change by submitting a rejected one.
                return AccountForms.Rejected(
                    http, string.Join(" ", changed.Errors.Select(e => e.Description)));
            }

            user.MustChangePassword = false;
            await users.UpdateAsync(user);

            // ChangePasswordAsync rotates the security stamp, which invalidates every
            // session including this one. Re-sign-in so the user is not bounced to the
            // login page immediately after succeeding.
            await signIn.RefreshSignInAsync(user);

            // Neither password reaches the log, only that this account replaced one. An
            // operator investigating a compromise needs the timeline, not the credential.
            AuthEventLog.Account(loggerFactory, AuthEvents.PasswordChanged, user.Email!);

            return Results.Redirect("/");
        });

        // No page at this route, so no "/submit" suffix is needed to avoid the
        // AmbiguousMatchException a static-SSR page endpoint produces -- a page answers POST
        // as well as GET. Nothing in the UI posts here yet: E02a ships no sign-out control,
        // so the only callers are the integration tests and an operator's own POST. The
        // route is on the enrolment gate's allowlist regardless, because a user who cannot
        // discharge their obligations must still be able to end the session.
        group.MapPost("/logout", async (
            HttpContext http,

            // Bound and never read. This endpoint carries no fields, and binding a form is
            // what attaches antiforgery validation to a minimal-API endpoint -- without the
            // parameter, this would be the one /account post a cross-site page could make.
            IFormCollection form,
            SignInManager<ApplicationUser> signIn,
            ILoggerFactory loggerFactory) =>
        {
            _ = form;

            // Read before the sign-out, which clears HttpContext.User's identity for the
            // remainder of the request.
            string email = ActorOf(http);

            await signIn.SignOutAsync();

            AuthEventLog.Account(loggerFactory, AuthEvents.SignedOut, email);

            return Results.Redirect("/account/login");
        });

        MapAdministrationEndpoints(group);
    }

    /// <summary>
    /// The mutating administration endpoints, all of them behind
    /// <see cref="Permissions.UsersManage"/> — a permission, never a role name, so the
    /// bundle of permissions a role carries can change without a deploy.
    /// <para>
    /// A nested group under <c>/account</c> rather than a group of its own, so these inherit
    /// the <c>account</c> rate-limit policy and its identity-plus-address partition. Nothing
    /// here needs a partitioner of its own.
    /// </para>
    /// <para>
    /// The page these post from lives at <c>/admin/users</c>, which is a different route
    /// from <c>/account/admin/*</c> and therefore does not collide with them — the same
    /// separation between a page and the endpoint its form posts to that the rest of this
    /// file relies on.
    /// </para>
    /// </summary>
    private static void MapAdministrationEndpoints(RouteGroupBuilder group)
    {
        RouteGroupBuilder admin = group.MapGroup("/admin")
            .RequireAuthorization(Permissions.UsersManage);

        admin.MapPost("/create-user", async (
            HttpContext http,
            IFormCollection form,
            UserManager<ApplicationUser> users,
            ILoggerFactory loggerFactory) =>
        {
            string email = form["email"].ToString().Trim();
            string displayName = form["displayName"].ToString().Trim();
            string password = form["password"].ToString();

            var user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                DisplayName = displayName,
                CreatedAt = DateTimeOffset.UtcNow,

                // Both obligations, and both are enforced by the enrolment gate rather than
                // by the redirect the sign-in handler issues. The account has no second
                // factor, and the administrator standing here knows the password -- so it
                // is shared until the user replaces it with one only they know.
                MustEnrolTotp = true,
                MustChangePassword = true,
            };

            IdentityResult created = await users.CreateAsync(user, password);

            if (!created.Succeeded)
            {
                return AccountForms.Rejected(
                    http, string.Join(" ", created.Errors.Select(error => error.Description)));
            }

            AuthEventLog.Administrative(loggerFactory, AuthEvents.AdminCreatedUser, ActorOf(http), email);

            return Results.Redirect("/admin/users");
        });

        admin.MapPost("/reset-password", async (
            HttpContext http,
            IFormCollection form,
            UserManager<ApplicationUser> users,
            IStringLocalizer<SharedResource> localizer,
            ILoggerFactory loggerFactory) =>
        {
            ApplicationUser? user = await users.FindByEmailAsync(form["email"].ToString().Trim());
            if (user is null)
            {
                return AccountForms.Rejected(http, NoSuchAccount(localizer));
            }

            // Generated and redeemed in the same breath. The token exists because Identity's
            // reset flow normally mails it; here the caller has already been authorised to
            // do this, so the round trip through the user is not what grants the authority.
            string token = await users.GeneratePasswordResetTokenAsync(user);
            IdentityResult reset = await users.ResetPasswordAsync(user, token, form["password"].ToString());

            if (!reset.Succeeded)
            {
                return AccountForms.Rejected(
                    http, string.Join(" ", reset.Errors.Select(error => error.Description)));
            }

            // A reset that left the account locked would hand the user a password they
            // cannot use, and the administrator no signal that they had not finished. The
            // failure count goes with it: leaving it behind means the next few honest
            // mistakes lock the account again immediately.
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);

            user.MustChangePassword = true;
            await users.UpdateAsync(user);

            // No explicit UpdateSecurityStampAsync here, and that is not an oversight:
            // ResetPasswordAsync rotates the stamp itself, which is what ends the sessions
            // held under the old password. Measured, not assumed -- removing the rotation
            // from set-lockout below reddens its test, and this path is covered by the
            // sign-in that follows the reset.
            //
            // Neither the new password nor the reset token above is logged. The token is a
            // bearer credential for exactly this operation.
            AuthEventLog.Administrative(loggerFactory, AuthEvents.AdminResetPassword, ActorOf(http), user.Email!);

            return Results.Redirect("/admin/users");
        });

        admin.MapPost("/clear-mfa", async (
            HttpContext http,
            IFormCollection form,
            UserManager<ApplicationUser> users,
            IStringLocalizer<SharedResource> localizer,
            ILoggerFactory loggerFactory) =>
        {
            ApplicationUser? user = await users.FindByEmailAsync(form["email"].ToString().Trim());
            if (user is null)
            {
                return AccountForms.Rejected(http, NoSuchAccount(localizer));
            }

            // This is the whole recovery path for a user who has lost both their
            // authenticator and their recovery codes: E02a has no regeneration page on
            // purpose. Re-enrolment calls GenerateNewTwoFactorRecoveryCodesAsync, which
            // REPLACES the stored set rather than adding to it, so the codes the user lost
            // stop working the moment they enrol again.
            await users.SetTwoFactorEnabledAsync(user, false);
            await users.ResetAuthenticatorKeyAsync(user);

            // Neither call above touches the recovery codes -- measured, not assumed: an
            // account with nine unspent codes still read nine after both of them. Those
            // codes are unreachable while TwoFactorEnabled is false, because the recovery
            // endpoint needs the two-factor cookie that only a two-factor challenge issues,
            // and re-enrolment would replace them. But "clear two-factor" is what an
            // administrator does when a user's second factor may be in somebody else's
            // hands, so leaving live credential material behind on the strength of an
            // argument about which other flag is currently false is the wrong default.
            // Generating zero codes replaces the stored set with an empty one.
            await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);

            user.MustEnrolTotp = true;
            await users.UpdateAsync(user);

            // Both calls above rotate the security stamp, so the cleared user's existing
            // sessions end on their next revalidation without anything explicit here.
            AuthEventLog.Administrative(loggerFactory, AuthEvents.AdminClearedMfa, ActorOf(http), user.Email!);

            return Results.Redirect("/admin/users");
        });

        admin.MapPost("/set-lockout", async (
            HttpContext http,
            IFormCollection form,
            UserManager<ApplicationUser> users,
            IStringLocalizer<SharedResource> localizer,
            ILoggerFactory loggerFactory) =>
        {
            ApplicationUser? user = await users.FindByEmailAsync(form["email"].ToString().Trim());
            if (user is null)
            {
                return AccountForms.Rejected(http, NoSuchAccount(localizer));
            }

            bool locked = string.Equals(form["locked"].ToString(), "true", StringComparison.OrdinalIgnoreCase);

            // DateTimeOffset.MaxValue is how Identity itself spells "until somebody lifts
            // it": IsLockedOutAsync compares the stored end against now, so an unreachable
            // end is an indefinite lock. A null end is the absence of one.
            IdentityResult result = await users.SetLockoutEndDateAsync(
                user, locked ? DateTimeOffset.MaxValue : null);

            if (!result.Succeeded)
            {
                // SetLockoutEndDateAsync refuses an account with LockoutEnabled false rather
                // than throwing, and silently redirecting to a page that still shows the
                // account unlocked would look like the click did nothing.
                return AccountForms.Rejected(
                    http, string.Join(" ", result.Errors.Select(error => error.Description)));
            }

            if (!locked)
            {
                await users.ResetAccessFailedCountAsync(user);
            }

            // THE line. Identity rotates the security stamp on a password reset and on a
            // two-factor change, but NOT on lockout -- and the stamp validator checks that
            // the cookie's stamp still MATCHES, which it does. So without this the locked
            // user's session survives for as long as the cookie lives, and the one-minute
            // ValidationInterval does not help: it decides how often to check, not what the
            // check would find. Locking without rotating is not locking.
            //
            // Rotating on the unlock too, deliberately: an administrator unlocking an
            // account after a suspected compromise gets the same guarantee, and one rule is
            // easier to keep true than two.
            await users.UpdateSecurityStampAsync(user);

            // Both edges, not just the lock. An operator asked "who gave this account access
            // back, and when" has no answer from a log that records only the taking away --
            // and an unlock after a suspected compromise is exactly the moment that question
            // gets asked.
            AuthEventLog.Administrative(
                loggerFactory,
                locked ? AuthEvents.AdminLockedUser : AuthEvents.AdminUnlockedUser,
                ActorOf(http),
                user.Email!);

            return Results.Redirect("/admin/users");
        });
    }

    /// <summary>
    /// The signed-in caller's e-mail address, for the <c>{Actor}</c> property of an
    /// administrative event. <c>Identity.Name</c> is the user name, which this application
    /// sets to the e-mail address.
    /// </summary>
    private static string ActorOf(HttpContext http) => http.User.Identity?.Name ?? "unknown";

    /// <summary>
    /// The e-mail address of the user holding a two-factor challenge, read from the
    /// two-factor cookie.
    /// <para>
    /// Must be called <b>before</b> the sign-in exchange. A successful
    /// <c>TwoFactorAuthenticatorSignInAsync</c> or <c>TwoFactorRecoveryCodeSignInAsync</c>
    /// deletes that cookie, so afterwards there is nobody left to name.
    /// </para>
    /// </summary>
    private static async Task<string> ChallengedEmailAsync(SignInManager<ApplicationUser> signIn)
    {
        ApplicationUser? challenged = await signIn.GetTwoFactorAuthenticationUserAsync();

        return challenged?.Email ?? "unknown";
    }

    /// <summary>
    /// "No account with that email address exists.", in the language of the request that
    /// asked.
    /// <para>
    /// The three administration endpoints that look a user up by e-mail address all end
    /// here, and the message reaches a person rather than an operator: it is rendered
    /// verbatim by <c>/admin/users</c>. Localizing the pages and leaving these three
    /// English would give a German administrator a German page carrying one English
    /// sentence — the only sentence on it they did not ask for.
    /// </para>
    /// <para>
    /// Resolved per call, not once: <see cref="IStringLocalizer"/> reads
    /// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> at lookup time, and
    /// <c>UseRequestLocalization</c> has set that from this request's <c>Accept-Language</c>
    /// well before any endpoint runs.
    /// </para>
    /// </summary>
    private static string NoSuchAccount(IStringLocalizer<SharedResource> localizer) =>
        localizer["Account_Error_NoSuchAccount"];

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
