using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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
    /// Arranged and asserted through the host's <b>key ring</b> and raw SQL rather than
    /// through <c>UserManager</c> and the fixture's <c>IdentityDbContext</c>.
    /// <para>
    /// That is not a stylistic preference. <c>IdentityDbContext.OnModelCreating</c>
    /// captures an <c>IDataProtector</c> into the value converter and EF caches the model
    /// per context type, so the first context built in a process fixes the ring for every
    /// later one — and this test process builds two, the host's database-backed ring and
    /// <c>PostgresFixture</c>'s <c>DataProtectionProvider.Create("Fakturenn.Tests")</c>,
    /// which lives on disk. Which one wins depends on class scheduling. Every in-process
    /// test is blind to it because they read back through the same captured protector;
    /// a subprocess is not, and this one failed with "the key … was not found in the key
    /// ring" whenever the disk ring won the race. Going straight to the ring the host
    /// persists to PostgreSQL is what both processes actually agree on.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Resetting_two_factor_clears_the_key_and_the_recovery_codes()
    {
        ApplicationUser user = await CreateUserAsync("reset-mfa@example.test");

        const string OriginalKey = "2W2NZBPUT2YX3LP3SUMMXICIO2INDYYU";
        await WriteTokenAsync(user.Id, "AuthenticatorKey", OriginalKey);
        await WriteTokenAsync(user.Id, "RecoveryCodes", "XBK77-435VP;TG5RD-6TJW9;QWVJ8-F983Q");
        await SetTwoFactorStateAsync(user.Id, twoFactorEnabled: true, mustEnrolTotp: false);

        (int exitCode, string output) = await RunAsync(["--reset-mfa", "reset-mfa@example.test"], standardInput: null);

        exitCode.Should().Be(0, output);

        ApplicationUser stored = await ReadUserAsync("reset-mfa@example.test");

        stored.TwoFactorEnabled.Should().BeFalse();
        stored.MustEnrolTotp.Should().BeTrue();
        (await ReadTokenAsync(user.Id, "AuthenticatorKey")).Should().NotBe(OriginalKey);

        // Reached for when a user's second factor may be in somebody else's hands, so no
        // live credential material may survive it. Neither SetTwoFactorEnabledAsync nor
        // ResetAuthenticatorKeyAsync touches the codes -- measured in Task 13 -- which is
        // why the command wipes them explicitly, exactly as /account/admin/clear-mfa does.
        (await ReadTokenAsync(user.Id, "RecoveryCodes")).Should().BeEmpty();
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

    /// <summary>
    /// Sets the two-factor columns directly, so the arrangement moves exactly what a
    /// completed enrolment moves and nothing else — the security stamp in particular
    /// stays put.
    /// </summary>
    private async Task SetTwoFactorStateAsync(Guid userId, bool twoFactorEnabled, bool mustEnrolTotp)
    {
        await using IdentityDbContext context = host.CreateIdentityContext();

        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(user => user.TwoFactorEnabled, twoFactorEnabled)
                    .SetProperty(user => user.MustEnrolTotp, mustEnrolTotp),
                TestContext.Current.CancellationToken);
    }

    /// <summary>Writes a token value protected with the ring the host persists to PostgreSQL.</summary>
    private async Task WriteTokenAsync(Guid userId, string name, string value)
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO identity."AspNetUserTokens" ("UserId", "LoginProvider", "Name", "Value")
            VALUES (@userId, '[AspNetUserStore]', @name, @value)
            ON CONFLICT ("UserId", "LoginProvider", "Name") DO UPDATE SET "Value" = @value
            """;
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("value", TokenProtector().Protect(value));

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Reads a token value back through the same ring, or null when there is no row.</summary>
    private async Task<string?> ReadTokenAsync(Guid userId, string name)
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT "Value" FROM identity."AspNetUserTokens"
            WHERE "UserId" = @userId AND "LoginProvider" = '[AspNetUserStore]' AND "Name" = @name
            """;
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("name", name);

        object? stored = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return stored is string ciphertext ? TokenProtector().Unprotect(ciphertext) : null;
    }

    private IDataProtector TokenProtector() =>
        host.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(IdentityDbContext.UserTokenProtectorPurpose);

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
