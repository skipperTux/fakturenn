using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fakturenn.Web.Operations;

/// <summary>
/// Recovery entrypoints for an operator locked out of the web interface. They require
/// host and database access rather than a password, which is exactly what makes them
/// both useful and unavailable to anyone who only reaches the web.
/// <para>
/// Every control this epic builds — authentication, the rate limiter, the enrolment
/// gate, the permission policies — is bypassed here, deliberately, because these exist
/// for the case where those controls have locked the operator out. Their safety rests
/// entirely on being reachable only from a shell on the host, so nothing in this file
/// may ever become reachable over HTTP.
/// </para>
/// <para>
/// A password is never taken as a command-line argument: argv is visible in <c>ps</c>
/// output and lands in shell history. Passwords are read from standard input.
/// </para>
/// </summary>
public static class OperatorCommands
{
    private const string CreateAdmin = "--create-admin";
    private const string ResetPassword = "--reset-password";
    private const string ResetMfa = "--reset-mfa";
    private const string UnlockUser = "--unlock-user";
    private const string ListUsers = "--list-users";

    /// <summary>Exit code for a caller error: a missing argument, or a refused request.</summary>
    private const int UsageExitCode = 2;

    /// <summary>Exit code for a request that was well formed but could not be carried out.</summary>
    private const int FailureExitCode = 1;

    private static readonly string[] _commands =
        [CreateAdmin, ResetPassword, ResetMfa, UnlockUser, ListUsers];

    /// <summary>
    /// Runs whichever operator command the arguments name, or answers <c>null</c> when
    /// they name none — in which case the caller goes on to serve traffic.
    /// </summary>
    public static async Task<int?> TryRunAsync(string[] args, WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(app);

        string? command = Array.Find(args, argument => _commands.Contains(argument, StringComparer.Ordinal));

        if (command is null)
        {
            return null;
        }

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        string? email = EmailAfter(args, command);

        return command switch
        {
            CreateAdmin => await CreateAdminAsync(users, db, email),
            ResetPassword => await ResetPasswordAsync(users, email),
            ResetMfa => await ResetMfaAsync(users, email),

            // Exists because the IsSystemRole guard prevents stripping the last
            // administrator's permissions but not LOCKING them. Without an unlock path
            // the guard protects the wrong thing.
            UnlockUser => await UnlockUserAsync(users, email),
            ListUsers => await ListUsersAsync(db),
            _ => null,
        };
    }

    /// <summary>
    /// The token following the command, unless it is itself a flag — so a mistyped
    /// <c>--reset-password --list-users</c> reports a missing address rather than
    /// hunting for a user called "--list-users".
    /// </summary>
    private static string? EmailAfter(string[] args, string command)
    {
        string? candidate = args.SkipWhile(argument => !string.Equals(argument, command, StringComparison.Ordinal))
            .Skip(1)
            .FirstOrDefault();

        return candidate is null || candidate.StartsWith('-') ? null : candidate;
    }

    private static async Task<int> CreateAdminAsync(
        UserManager<ApplicationUser> users, IdentityDbContext db, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            await Console.Error.WriteLineAsync($"{CreateAdmin} requires an email address.");
            return UsageExitCode;
        }

        string? password = ReadPassword(CreateAdmin);
        if (password is null)
        {
            return UsageExitCode;
        }

        // An explicit transaction under a DbContext configured with EnableRetryOnFailure
        // (see IdentityConfiguration) must go through the execution strategy, or EF
        // throws InvalidOperationException. The delegate can therefore re-run, so the
        // user is built inside it rather than captured from outside.
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            async token =>
            {
                await using IDbContextTransaction transaction =
                    await db.Database.BeginTransactionAsync(token);

                // The same key /account/setup takes, which is the whole point: this
                // entrypoint and that page are alternative routes to one state, and a
                // Job racing the page — or two Jobs racing each other — would otherwise
                // both pass the "no users" check and both mint an administrator.
                await SetupLock.TakeAsync(db, token);

                if (await db.Users.AnyAsync(token))
                {
                    await Console.Error.WriteLineAsync(
                        $"Refusing: an account already exists. Use {ResetPassword} instead.");
                    return UsageExitCode;
                }

                var user = new ApplicationUser
                {
                    Id = Guid.CreateVersion7(),
                    UserName = email,
                    Email = email,
                    DisplayName = email,
                    CreatedAt = DateTimeOffset.UtcNow,

                    // The CLI path never produces an account without a second factor:
                    // enrolment is completed at first sign-in, forced by the enrolment
                    // gate rather than by a redirect. MustChangePassword goes with it
                    // because whoever ran this command knows the password — an
                    // unattended install reads it from a secret, and it stops being
                    // shared the moment it is first used.
                    MustEnrolTotp = true,
                    MustChangePassword = true,
                };

                IdentityResult created = await users.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    await Console.Error.WriteLineAsync(Describe(created));
                    return FailureExitCode;
                }

                // A re-sync, not create-if-absent, and cheap when --migrate has already
                // run: an operator who reaches for this entrypoint on a fresh instance
                // must not end up with an administrator holding no permissions.
                await RoleSeeder.SeedAsync(db, token);

                Guid administratorRoleId = await db.Roles
                    .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
                    .Select(role => role.Id)
                    .SingleAsync(token);

                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = administratorRoleId });
                await db.SaveChangesAsync(token);

                await transaction.CommitAsync(token);

                Console.WriteLine(
                    $"Created administrator {email}. TOTP enrolment and a password change are required at first sign-in.");

                return 0;
            },
            CancellationToken.None);
    }

    private static async Task<int> ResetPasswordAsync(UserManager<ApplicationUser> users, string? email)
    {
        (ApplicationUser? user, int? failure) = await FindAsync(users, email, ResetPassword);
        if (user is null)
        {
            return failure!.Value;
        }

        // Read after the lookup, so an unknown address fails without an operator having
        // typed a password first.
        string? password = ReadPassword(ResetPassword);
        if (password is null)
        {
            return UsageExitCode;
        }

        // Generated and redeemed in the same breath. The token exists because Identity's
        // reset flow normally mails it; here the authority came from having a shell on
        // the host, so the round trip through the user grants nothing extra.
        string token = await users.GeneratePasswordResetTokenAsync(user);
        IdentityResult reset = await users.ResetPasswordAsync(user, token, password);

        if (!reset.Succeeded)
        {
            await Console.Error.WriteLineAsync(Describe(reset));
            return FailureExitCode;
        }

        // A reset that leaves the account locked is a surprise: an operator resetting a
        // password almost always wants the account usable afterwards. The failure count
        // goes with it, or the next few honest mistakes lock it again immediately.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);

        user.MustChangePassword = true;
        await users.UpdateAsync(user);

        // No explicit UpdateSecurityStampAsync: ResetPasswordAsync rotates the stamp
        // itself, which is what ends the sessions held under the old password.
        Console.WriteLine($"Password reset for {email}. A password change is required at next sign-in.");
        return 0;
    }

    private static async Task<int> ResetMfaAsync(UserManager<ApplicationUser> users, string? email)
    {
        (ApplicationUser? user, int? failure) = await FindAsync(users, email, ResetMfa);
        if (user is null)
        {
            return failure!.Value;
        }

        // The recovery path for a user who has lost both their authenticator and their
        // recovery codes; E02a has no regeneration page on purpose.
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);

        // Neither call above touches the recovery codes -- measured in Task 13, not
        // assumed: an account with nine unspent codes still read nine after both. They
        // are unreachable while TwoFactorEnabled is false, and re-enrolment would replace
        // them; but an operator clearing two-factor is often doing so because the old
        // factors may be in somebody else's hands, so leaving live credential material
        // behind on the strength of an argument about another flag is the wrong default.
        // Generating zero replaces the stored set with an empty one. The administration
        // endpoint at /account/admin/clear-mfa does exactly this, and the two paths must
        // not behave differently.
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);

        user.MustEnrolTotp = true;
        await users.UpdateAsync(user);

        // Both calls above rotate the security stamp, so the cleared user's existing
        // sessions end on their next revalidation without anything explicit here --
        // measured in Task 10 (HQ6QNBU3... -> BK4GZJDP...).
        Console.WriteLine(
            $"Two-factor authentication cleared for {email}. Re-enrolment is required at next sign-in.");
        return 0;
    }

    private static async Task<int> UnlockUserAsync(UserManager<ApplicationUser> users, string? email)
    {
        (ApplicationUser? user, int? failure) = await FindAsync(users, email, UnlockUser);
        if (user is null)
        {
            return failure!.Value;
        }

        // Null is the absence of a lockout end; DateTimeOffset.MaxValue is how Identity
        // spells an indefinite one.
        IdentityResult unlocked = await users.SetLockoutEndDateAsync(user, null);
        if (!unlocked.Succeeded)
        {
            // SetLockoutEndDateAsync refuses an account with LockoutEnabled false rather
            // than throwing, and reporting success there would send the operator away
            // believing a locked-out administrator can sign in again.
            await Console.Error.WriteLineAsync(Describe(unlocked));
            return FailureExitCode;
        }

        // Leaving the count behind means the next few honest mistakes lock the account
        // again immediately -- though note AccessFailedCount reads 0 on an account that
        // is already locked: reaching the limit sets LockoutEnd and zeroes the counter in
        // the same call.
        await users.ResetAccessFailedCountAsync(user);

        // No security-stamp rotation here, unlike the locking path. Rotating exists to
        // end a session that must stop working; unlocking grants access rather than
        // revoking it, and the sessions the lock ended are already gone.
        Console.WriteLine($"Unlocked {email}.");
        return 0;
    }

    /// <summary>
    /// Diagnostic only. It prints <b>no secret</b> — no authenticator key, no recovery
    /// code, no password hash. A command that dumps credentials to a terminal and from
    /// there into a shell's scrollback and an operator's log is its own vulnerability,
    /// and it would be reached for precisely when an instance is already in trouble.
    /// </summary>
    private static async Task<int> ListUsersAsync(IdentityDbContext db)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var listed = await db.Users.AsNoTracking()
            .OrderBy(user => user.Email)
            .Select(user => new
            {
                user.Email,
                user.DisplayName,
                user.TwoFactorEnabled,
                user.MustEnrolTotp,
                user.LockoutEnd,
            })
            .ToListAsync();

        foreach (var user in listed)
        {
            Console.WriteLine(
                $"{user.Email}\t{user.DisplayName}\tlocked={user.LockoutEnd > now}\t"
                + $"lockedUntil={user.LockoutEnd?.ToString("u", null) ?? "-"}\t"
                + $"twoFactor={user.TwoFactorEnabled}\tmustEnrol={user.MustEnrolTotp}");
        }

        Console.WriteLine($"{listed.Count} user(s).");

        // Success even when the list is empty: "no users exist" is a true answer to the
        // question, and it is the answer an operator checking whether /setup is still
        // open most wants.
        return 0;
    }

    private static async Task<(ApplicationUser? User, int? Failure)> FindAsync(
        UserManager<ApplicationUser> users, string? email, string command)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            await Console.Error.WriteLineAsync($"{command} requires an email address.");
            return (null, UsageExitCode);
        }

        ApplicationUser? user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            // Deliberately explicit, unlike the sign-in endpoints: this caller already
            // has the database, so there is no enumeration oracle to protect, and an
            // operator needs to know a typo from a missing account.
            await Console.Error.WriteLineAsync($"No user with email {email}.");
            return (null, FailureExitCode);
        }

        return (user, null);
    }

    /// <summary>
    /// Reads a password from standard input. Never from <c>args</c>: argv shows up in
    /// <c>ps</c> and in shell history, and a credential that has been in either is
    /// already compromised.
    /// </summary>
    private static string? ReadPassword(string command)
    {
        // The prompt goes to standard error so it does not contaminate the output of a
        // command whose stdout an operator may be piping somewhere.
        Console.Error.Write("Password: ");
        string? password = Console.ReadLine();

        if (!string.IsNullOrWhiteSpace(password))
        {
            return password;
        }

        Console.Error.WriteLine(
            $"{command} reads the password from standard input, and none was supplied. "
            + "Pipe it in; do not pass it as an argument.");

        return null;
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(error => error.Description));
}
