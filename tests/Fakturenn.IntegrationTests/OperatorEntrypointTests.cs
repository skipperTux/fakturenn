using System.Net;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// The operator recovery entrypoints, run as real subprocesses against the fixture's
/// database.
/// <para>
/// They are dispatched from <c>Program.cs</c>'s top-level statements, which nothing
/// in-process can reach. A test over <c>OperatorCommands</c> alone would prove the
/// methods work and say nothing about whether the entrypoint calls them — and the
/// dispatch is the part that silently disappears. Each test here therefore asserts the
/// exit code <b>and</b> the effect in the database.
/// </para>
/// <para>
/// The host built by the fixture is left running throughout. That is deliberate: these
/// commands are meant to be usable against a live instance, and the subprocess taking the
/// setup advisory lock while a host serves <c>/setup</c> is the race the lock exists for.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed class OperatorEntrypointTests(SetupHostFixture host)
{
    /// <summary>Satisfies the configured policy: twelve characters, upper, lower, digit.</summary>
    private const string Password = "Korrekt-Pferd-42";

    private const string ReplacementPassword = "Anderes-Pferd-77";

    /// <summary>A caller error — a missing argument, or a refused request.</summary>
    private const int UsageExitCode = 2;

    /// <summary>
    /// A page that needs nothing but an authenticated user, so a 302 away from it means the
    /// session ended rather than that a permission was missing. It is also on the enrolment
    /// gate's allowlist, so an unenrolled account can serve as the subject.
    /// </summary>
    private const string SessionProbe = "/account/change-password";

    [Fact]
    public async Task Creating_the_first_administrator_sets_both_obligations_and_grants_the_role()
    {
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        (int exitCode, string output) = await RunAsync(["--create-admin", "ops@example.test"], Password);

        exitCode.Should().Be(0, output);

        ApplicationUser created = await ReadUserAsync("ops@example.test");

        // Neither obligation is optional: the CLI path must never produce an account with
        // one factor, and whoever ran the command knows the password.
        created.MustEnrolTotp.Should().BeTrue();
        created.MustChangePassword.Should().BeTrue();

        // The password came off standard input, not out of thin air — this is what proves
        // the pipe was actually consumed.
        (await CheckPasswordAsync(created, Password)).Should().BeTrue();

        await using IdentityDbContext context = host.CreateIdentityContext();
        Guid administratorRoleId = await context.Roles
            .Where(role => role.Name == RoleSeeder.AdministratorRoleName)
            .Select(role => role.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        bool assigned = await context.UserRoles.AsNoTracking()
            .AnyAsync(
                userRole => userRole.UserId == created.Id && userRole.RoleId == administratorRoleId,
                TestContext.Current.CancellationToken);

        // An administrator with no permissions is worse than no administrator: the
        // instance looks configured and nothing can be administered.
        assigned.Should().BeTrue();
    }

    [Fact]
    public async Task Creating_an_administrator_on_an_instance_that_already_has_users_is_refused()
    {
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);
        await host.CreateUserAsync("incumbent@example.test", Password, TestContext.Current.CancellationToken);

        (int exitCode, string output) = await RunAsync(["--create-admin", "usurper@example.test"], Password);

        // The same guard /setup applies, taken under the same advisory lock. Silently
        // minting a second administrator would hand full control of an instance to anyone
        // who could run one command.
        exitCode.Should().Be(UsageExitCode, output);

        await using IdentityDbContext context = host.CreateIdentityContext();
        bool exists = await context.Users.AsNoTracking()
            .AnyAsync(user => user.Email == "usurper@example.test", TestContext.Current.CancellationToken);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_create_admin_runs_against_an_empty_instance_produce_exactly_one_user()
    {
        // The reason this entrypoint takes /account/setup's advisory lock rather than its
        // own guard: "no users exist" and the insert are not atomic, and the window
        // between them holds a password hash and several round trips. Two Kubernetes
        // Jobs, or a Job racing an operator at the page, both pass the check.
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        const int Racers = 4;

        Task<(int ExitCode, string Output)>[] runs =
        [
            .. Enumerable.Range(0, Racers)
                .Select(index => RunAsync(["--create-admin", $"racer-{index}@example.test"], Password)),
        ];

        await Task.WhenAll(runs);

        await using IdentityDbContext context = host.CreateIdentityContext();

        List<string?> emails = await context.Users.AsNoTracking()
            .Select(user => user.Email)
            .OrderBy(email => email)
            .ToListAsync(TestContext.Current.CancellationToken);

        emails.Should().ContainSingle(
            "an entrypoint that mints the first administrator must produce one administrator, "
            + $"but it produced: {string.Join(", ", emails)}");

        // Exactly one winner, and every loser says so rather than exiting 0 having done
        // nothing: an unattended install must be able to tell which Job created the
        // account it is about to hand credentials to.
        runs.Count(run => run.Result.ExitCode == 0).Should().Be(1);
    }

    /// <summary>
    /// argv is visible in <c>ps</c> output and lands in shell history, so a credential
    /// that has been there is already compromised. No flag may accept one: supplying a
    /// password on the command line and nothing on standard input has to fail, whether it
    /// is offered positionally or under a flag name of its own.
    /// </summary>
    [Theory]
    [InlineData("--create-admin", "argv@example.test", Password)]
    [InlineData("--create-admin", "argv@example.test", "--password", Password)]
    public async Task A_password_supplied_on_the_command_line_is_not_accepted(params string[] arguments)
    {
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        (int exitCode, string output) = await RunAsync(arguments, standardInput: null);

        exitCode.Should().NotBe(0, output);

        await using IdentityDbContext context = host.CreateIdentityContext();
        bool exists = await context.Users.AsNoTracking()
            .AnyAsync(user => user.Email == "argv@example.test", TestContext.Current.CancellationToken);

        exists.Should().BeFalse(output);
    }

    [Fact]
    public async Task Creating_an_administrator_with_nothing_on_standard_input_is_refused()
    {
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        (int exitCode, string output) = await RunAsync(
            ["--create-admin", "nostdin@example.test"], standardInput: null);

        exitCode.Should().Be(UsageExitCode, output);
        output.Should().Contain("standard input");
    }

    [Fact]
    public async Task Resetting_a_password_replaces_it_clears_the_lockout_and_forces_a_change()
    {
        ApplicationUser user = await CreateUserAsync("reset-password@example.test");
        await LockAsync(user.Id, failedAttempts: 4);

        (int exitCode, string output) = await RunAsync(
            ["--reset-password", "reset-password@example.test"], ReplacementPassword);

        exitCode.Should().Be(0, output);

        ApplicationUser stored = await ReadUserAsync("reset-password@example.test");

        (await CheckPasswordAsync(stored, ReplacementPassword)).Should().BeTrue();
        (await CheckPasswordAsync(stored, Password)).Should().BeFalse();

        // A reset that leaves the account locked hands the user a password they cannot
        // use. The failure count goes with the lockout, or the next few honest mistakes
        // lock it again immediately.
        stored.LockoutEnd.Should().BeNull();
        stored.AccessFailedCount.Should().Be(0);

        // The operator chose this password, so it is shared until the user replaces it.
        stored.MustChangePassword.Should().BeTrue();
    }

    /// <summary>
    /// Arranged through the host and asserted through the host, the ordinary way.
    /// <para>
    /// This test spent one round trip going through raw SQL and the host's key ring
    /// instead, to dodge <c>IdentityDbContext</c> sharing one cached model — and therefore
    /// one <c>IDataProtector</c> — between the host's database-backed ring and
    /// <c>PostgresFixture</c>'s on-disk one. <c>UserTokenProtectorModelCacheKeyFactory</c>
    /// fixed that, so the workaround went with it: a token this process writes is now a
    /// token the subprocess can read, which is what the test was asserting all along.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Resetting_two_factor_clears_the_key_and_the_recovery_codes()
    {
        ApplicationUser user = await CreateUserAsync("reset-mfa@example.test");
        string originalKey = await host.EnableTwoFactorAsync(user.Id);
        await host.GenerateRecoveryCodesAsync(user.Id, 10);

        (int exitCode, string output) = await RunAsync(["--reset-mfa", "reset-mfa@example.test"], standardInput: null);

        exitCode.Should().Be(0, output);

        ApplicationUser stored = await ReadUserAsync("reset-mfa@example.test");

        stored.TwoFactorEnabled.Should().BeFalse();
        stored.MustEnrolTotp.Should().BeTrue();
        (await host.ReadAuthenticatorKeyAsync(user.Id)).Should().NotBe(originalKey);

        // Reached for when a user's second factor may be in somebody else's hands, so no
        // live credential material may survive it. Neither SetTwoFactorEnabledAsync nor
        // ResetAuthenticatorKeyAsync touches the codes -- measured in Task 13 -- which is
        // why the command wipes them explicitly, exactly as /account/admin/clear-mfa does.
        (await host.CountRecoveryCodesAsync(user.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Unlocking_a_user_clears_the_lockout_and_the_failure_count()
    {
        ApplicationUser user = await CreateUserAsync("unlock@example.test");
        await LockAsync(user.Id, failedAttempts: 5);

        (int exitCode, string output) = await RunAsync(["--unlock-user", "unlock@example.test"], standardInput: null);

        exitCode.Should().Be(0, output);

        ApplicationUser stored = await ReadUserAsync("unlock@example.test");

        // This is the whole reason the command exists: locking the last administrator is
        // permitted, and without an unlock path there is no route back into the instance.
        stored.LockoutEnd.Should().BeNull();
        stored.AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task Unlocking_a_user_ends_the_session_they_already_hold()
    {
        // The case that makes this a rotation rather than a courtesy: an account locked by
        // FAILED ATTEMPTS never had its stamp rotated, because Identity's automatic lockout
        // does not touch it. So a session opened before those failures is still live when
        // the operator unlocks the account -- and whoever was guessing the password is
        // exactly who might be holding it. LockAsync arranges that state precisely: the
        // lockout columns move and nothing else does.
        ApplicationUser user = await CreateUserAsync("unlock-session@example.test");

        // Issued two minutes ago, the only artificial thing here: the stamp validator
        // revalidates only once its one-minute interval has elapsed since IssuedUtc, so a
        // cookie minted "now" would sail through the next request whatever the command did.
        Cookie session = await host.CreateAuthenticationCookieAsync(
            user, DateTimeOffset.UtcNow.AddMinutes(-2));

        await LockAsync(user.Id, failedAttempts: 5);

        using (HttpClient before = ClientWith(session))
        {
            using HttpResponseMessage reachable = await before.GetAsync(
                SessionProbe, TestContext.Current.CancellationToken);

            reachable.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "the session has to work before unlocking can be shown to end it -- automatic "
                + "lockout leaves it working, which is the whole point");
        }

        (int exitCode, string output) = await RunAsync(
            ["--unlock-user", "unlock-session@example.test"], standardInput: null);

        exitCode.Should().Be(0, output);

        // A fresh jar holding the same ticket. A successful revalidation sets ShouldRenew
        // and the handler reissues the cookie with a current IssuedUtc, so reusing the jar
        // above would hand this request a freshly issued ticket that skips revalidation
        // entirely -- and the test would fail against correct code.
        using HttpClient after = ClientWith(session);
        using HttpResponseMessage bounced = await after.GetAsync(
            SessionProbe, TestContext.Current.CancellationToken);

        bounced.StatusCode.Should().Be(HttpStatusCode.Found);
        LocationPath(bounced).Should().Be(
            "/account/login", "unlocking must end the session the account already held");
    }

    [Fact]
    public async Task Listing_users_reports_their_state_and_prints_no_secret()
    {
        await host.ResetUsersAsync(TestContext.Current.CancellationToken);

        ApplicationUser user = await CreateUserAsync("listed@example.test");
        await SetDisplayNameAsync(user.Id, "Listed Operator");
        string authenticatorKey = await host.EnableTwoFactorAsync(user.Id);
        string[] recoveryCodes = await host.GenerateRecoveryCodesAsync(user.Id, 10);
        await LockAsync(user.Id, failedAttempts: 5);

        (int exitCode, string output) = await RunAsync(["--list-users"], standardInput: null);

        exitCode.Should().Be(0, output);

        output.Should().Contain("listed@example.test");
        output.Should().Contain("Listed Operator");
        output.Should().Contain("locked=True");
        output.Should().Contain("twoFactor=True");
        output.Should().Contain("1 user(s).");

        // The part that matters. A diagnostic command an operator runs when an instance is
        // already in trouble, whose output lands in a terminal's scrollback and from there
        // into a support ticket, must not carry credentials out with it.
        string passwordHash = await ReadPasswordHashAsync(user.Id);

        output.Should().NotContain(authenticatorKey);
        output.Should().NotContain(passwordHash);

        foreach (string recoveryCode in recoveryCodes)
        {
            output.Should().NotContain(recoveryCode);
        }
    }

    [Theory]
    [InlineData("--reset-password")]
    [InlineData("--reset-mfa")]
    [InlineData("--unlock-user")]
    public async Task An_address_no_account_holds_fails_rather_than_reporting_success(string command)
    {
        // A command that exits 0 having done nothing is worse than one that fails: an
        // operator reading a typo as success walks away from a locked-out instance
        // believing they have fixed it.
        (int exitCode, string output) = await RunAsync([command, "nobody@example.test"], Password);

        exitCode.Should().NotBe(0, output);
        output.Should().Contain("nobody@example.test");
    }

    [Theory]
    [InlineData("--create-admin")]
    [InlineData("--reset-password")]
    [InlineData("--reset-mfa")]
    [InlineData("--unlock-user")]
    public async Task A_command_with_no_address_reports_a_usage_error(string command)
    {
        (int exitCode, string output) = await RunAsync([command], Password);

        exitCode.Should().Be(UsageExitCode, output);
        output.Should().Contain("requires an email address");
    }

    private static string LocationPath(HttpResponseMessage response)
    {
        Uri location = response.Headers.Location
            ?? throw new InvalidOperationException("A redirect with no Location header.");

        return location.IsAbsoluteUri
            ? location.AbsolutePath
            : location.OriginalString.Split('?')[0];
    }

    private HttpClient ClientWith(Cookie session)
    {
        CookieContainer cookies = new();
        cookies.Add(new Uri(host.BaseAddress), session);

        return host.CreateClient(cookies);
    }

    private Task<(int ExitCode, string Output)> RunAsync(string[] arguments, string? standardInput) =>
        HostProcess.RunAsync(
            host.ConnectionString, arguments, standardInput, TestContext.Current.CancellationToken);

    private async Task<ApplicationUser> CreateUserAsync(string email)
    {
        await using (IdentityDbContext context = host.CreateIdentityContext())
        {
            // These tests share one database with every other class in the collection, and
            // a leftover from an earlier run would make CreateUserAsync fail on the unique
            // user name rather than on anything this test is about.
            await context.Users.Where(user => user.Email == email)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        return await host.CreateUserAsync(email, Password, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Locks an account by column update rather than through <see cref="UserManager{TUser}"/>,
    /// so the arrangement is exactly the state five failed sign-ins leave behind and
    /// nothing else moves — in particular the security stamp stays put.
    /// </summary>
    private async Task LockAsync(Guid userId, int failedAttempts)
    {
        await using IdentityDbContext context = host.CreateIdentityContext();

        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(user => user.LockoutEnd, DateTimeOffset.MaxValue)
                    .SetProperty(user => user.AccessFailedCount, failedAttempts),
                TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Gives the account a display name distinct from its address, so asserting that the
    /// listing shows one is not silently satisfied by the other.
    /// </summary>
    private async Task SetDisplayNameAsync(Guid userId, string displayName)
    {
        await using IdentityDbContext context = host.CreateIdentityContext();

        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(user => user.DisplayName, displayName),
                TestContext.Current.CancellationToken);
    }

    private async Task<ApplicationUser> ReadUserAsync(string email)
    {
        await using IdentityDbContext context = host.CreateIdentityContext();

        return await context.Users.AsNoTracking()
            .SingleAsync(user => user.Email == email, TestContext.Current.CancellationToken);
    }

    private async Task<string> ReadPasswordHashAsync(Guid userId)
    {
        await using IdentityDbContext context = host.CreateIdentityContext();

        return await context.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.PasswordHash!)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies a password through the host's own configured hasher, so a pass here means
    /// the sign-in endpoint would accept it too.
    /// </summary>
    private async Task<bool> CheckPasswordAsync(ApplicationUser user, string password)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();

        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser stored = await users.FindByIdAsync(user.Id.ToString())
            ?? throw new InvalidOperationException($"No user with id {user.Id}.");

        return await users.CheckPasswordAsync(stored, password);
    }
}
