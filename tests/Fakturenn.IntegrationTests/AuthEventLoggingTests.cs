using System.Globalization;
using System.Net;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Serilog.Events;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// The authentication event log, read from a real Serilog sink attached to the running
/// host (<see cref="HostLogCapture"/>).
/// <para>
/// Two properties are under test, and they pull in opposite directions. An operator must be
/// able to answer "is someone attacking this instance", which means the events have to be
/// there; and no event may carry a credential, which means what is there has to be
/// harmless. A suite that only asserted the first would be satisfied by a log that
/// helpfully echoed every password.
/// </para>
/// <para>
/// The leakage test was proved capable of failing before it was trusted: adding
/// <c>logger.LogInformation("leak {P}", password)</c> to the sign-in handler turned it red,
/// which is the only evidence that a green run means anything. Same reasoning as the
/// incidental-serialisation false green found in Task 9.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed class AuthEventLoggingTests(SetupHostFixture host)
{
    /// <summary>Satisfies the configured policy: twelve characters, upper, lower, digit.</summary>
    private const string Password = "Korrekt-Pferd-42";

    private const string ReplacementPassword = "Anderes-Pferd-77";

    private const string ResetPassword = "Drittes-Pferd-13";

    /// <summary>
    /// Everything an authentication event is allowed to carry.
    /// <para>
    /// The first four come from the message templates in <c>AuthEventLog</c>. The rest are
    /// ambient: <c>SourceContext</c> and <c>EventId</c> from Serilog's <c>ILogger</c>
    /// adapter, and <c>RequestId</c>, <c>RequestPath</c> and <c>ConnectionId</c> from
    /// ASP.NET Core's own request logging scope. Those three are correlation handles, not
    /// credentials — <c>RequestPath</c> is <c>Request.Path</c> with no query string, and
    /// <c>ConnectionId</c> names a Kestrel connection rather than an authenticated session,
    /// so none of them lets a reader of the log reconstruct anybody's session.
    /// </para>
    /// <para>
    /// This is the half of the leakage check that survives an event nobody has thought of
    /// yet: a password-reset token, a security stamp or a Data Protection payload added to
    /// a template later has a name, and any name outside this set fails here even though no
    /// test knows its value.
    /// </para>
    /// </summary>
    private static readonly string[] _permittedProperties =
    [
        "Event",
        "Email",
        "Actor",
        "Target",
        "SourceContext",
        "EventId",
        "RequestId",
        "RequestPath",
        "ConnectionId",
    ];

    [Fact]
    public async Task No_secret_reaches_a_sink()
    {
        List<string> secrets = [Password, ReplacementPassword, ResetPassword];
        int mark = HostLogCapture.Instance.Mark();

        // 1. Enrolment, through the real endpoint. The authenticator key, the code proving
        //    possession of it, and the data-protected cookie carrying the recovery codes to
        //    the page that shows them are all in play on this one request.
        ApplicationUser enrolling = await host.CreateUserAsync("log-enrol@example.test", Token);
        using (HttpClient client = ClientFor(await host.CreateAuthenticationCookieAsync(enrolling)))
        {
            // The page is what generates the key, so it has to be rendered before there is
            // one to read.
            using (HttpResponseMessage page = await GetAsync(client, "/account/enrol-totp"))
            {
                page.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            string key = await host.ReadAuthenticatorKeyAsync(enrolling.Id);
            string code = SignInHelper.CurrentCode(key);
            secrets.Add(key);
            secrets.Add(code);

            using HttpResponseMessage enrolled =
                await SignInHelper.PostCodeAsync(client, "/account/enrol-totp/verify", code);

            enrolled.Headers.Location?.OriginalString.Should().Be("/account/recovery-codes");
            secrets.Add(RecoveryCookie(enrolled));
        }

        // 2. A real password-plus-TOTP sign-in, followed by a password change.
        ApplicationUser signingIn = await host.CreateUserAsync("log-signin@example.test", Password, Token);
        string signInKey = await host.EnableTwoFactorAsync(signingIn.Id);
        secrets.Add(signInKey);
        secrets.Add(SignInHelper.CurrentCode(signInKey));

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        {
            await SignInHelper.SignInAsync(client, signingIn.UserName!, Password, signInKey);

            using HttpResponseMessage changed = await PostAsync(
                client,
                "/account/change-password/submit",
                ("currentPassword", Password),
                ("newPassword", ReplacementPassword));

            changed.Headers.Location?.OriginalString.Should().Be("/");
        }

        // 3. A recovery code being redeemed. Issued through the host so the test knows the
        //    plaintext it is looking for; spent through the endpoint that accepts them.
        ApplicationUser recovering = await host.CreateUserAsync("log-recovery@example.test", Password, Token);
        string recoveryKey = await host.EnableTwoFactorAsync(recovering.Id);
        string[] recoveryCodes = await host.GenerateRecoveryCodesAsync(recovering.Id, 10);
        secrets.Add(recoveryKey);
        secrets.AddRange(recoveryCodes);

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        {
            using (HttpResponseMessage passwordStep =
                await SignInHelper.PostPasswordAsync(client, recovering.UserName!, Password))
            {
                passwordStep.Headers.Location?.OriginalString.Should().Be("/account/login-2fa");
            }

            using HttpResponseMessage redeemed = await SignInHelper.PostCodeAsync(
                client, "/account/login-recovery/submit", recoveryCodes[0]);

            redeemed.Headers.Location?.OriginalString.Should().Be("/");
        }

        // 4. An administrator resetting somebody's password. The endpoint mints a
        //    password-reset token internally; no test can name it, which is what the
        //    property allow-list below is for.
        ApplicationUser administrator = await host.CreateUserAsync("log-admin@example.test", Password, Token);
        await host.AssignAdministratorRoleAsync(administrator.Id, Token);
        string administratorKey = await host.EnableTwoFactorAsync(administrator.Id);
        secrets.Add(administratorKey);

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        {
            await SignInHelper.SignInAsync(client, administrator.UserName!, Password, administratorKey);

            using HttpResponseMessage reset = await PostAsync(
                client,
                "/account/admin/reset-password",
                ("email", signingIn.Email!),
                ("password", ResetPassword));

            reset.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        // Security stamps are read last, so they are the ones the four flows above left
        // behind rather than the ones they started with.
        foreach (ApplicationUser user in new[] { enrolling, signingIn, recovering, administrator })
        {
            secrets.Add(await host.ReadSecurityStampAsync(user.Id, Token));
        }

        IReadOnlyList<LogEvent> written = HostLogCapture.Instance.Since(mark);
        written.Should().NotBeEmpty("the flows above must have produced log events to examine");

        string captured = Flatten(written);

        foreach (string secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            captured.Should().NotContain(
                secret,
                "no password, TOTP code, recovery code, authenticator key, security stamp or "
                + "data-protection payload may reach a log sink");
        }

        // The allow-list catches what the value scan cannot: a secret this test has no way
        // to name, added to a template by a later change.
        foreach (LogEvent authEvent in AuthEventsIn(written))
        {
            authEvent.Properties.Keys.Should().BeSubsetOf(
                _permittedProperties,
                "an authentication event carries an event name and an identity, nothing else");
        }
    }

    [Fact]
    public async Task A_refused_sign_in_is_logged_without_saying_which_half_was_wrong()
    {
        ApplicationUser user = await host.CreateUserAsync("log-refused@example.test", Password, Token);
        int mark = HostLogCapture.Instance.Mark();

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        {
            using HttpResponseMessage refused =
                await SignInHelper.PostPasswordAsync(client, user.UserName!, "Falsches-Pferd-99");

            refused.Headers.Location?.OriginalString.Should().Be("/account/login?error=invalid");
        }

        LogEvent logged = SingleEvent(mark, "SignInFailed");

        logged.Level.Should().Be(LogEventLevel.Warning, "a refused sign-in is what an operator watches for");
        Property(logged, "Email").Should().Be(user.Email);

        // Task 11 proved the endpoint answers identically for an unknown account and a wrong
        // password. The log must keep that silence: anyone who can read it would otherwise
        // hold the enumeration oracle the endpoint refuses to hand out.
        logged.Properties.Should().NotContainKey("Reason");
    }

    [Fact]
    public async Task An_unknown_address_is_logged_exactly_like_a_wrong_password()
    {
        int mark = HostLogCapture.Instance.Mark();

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        {
            using HttpResponseMessage refused =
                await SignInHelper.PostPasswordAsync(client, "log-nobody@example.test", Password);

            refused.Headers.Location?.OriginalString.Should().Be("/account/login?error=invalid");
        }

        LogEvent logged = SingleEvent(mark, "SignInFailed");

        // Same event name, same level, same property set as the test above. Two shapes here
        // would be two shapes an attacker could tell apart.
        logged.Level.Should().Be(LogEventLevel.Warning);
        Property(logged, "Email").Should().Be("log-nobody@example.test");
        logged.Properties.Keys.Should().BeSubsetOf(_permittedProperties);
    }

    [Fact]
    public async Task A_locked_account_meeting_the_sign_in_endpoint_is_logged()
    {
        ApplicationUser victim = await host.CreateUserAsync("log-locked@example.test", Password, Token);
        using HttpClient admin = await AdministratorClientAsync("log-lock-admin@example.test");

        using (HttpResponseMessage locked = await PostAsync(
            admin, "/account/admin/set-lockout", ("email", victim.Email!), ("locked", "true")))
        {
            locked.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        int mark = HostLogCapture.Instance.Mark();

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        {
            using HttpResponseMessage refused =
                await SignInHelper.PostPasswordAsync(client, victim.UserName!, Password);

            refused.Headers.Location?.OriginalString.Should().Be("/account/lockout");
        }

        LogEvent logged = SingleEvent(mark, "AccountLockedOut");

        logged.Level.Should().Be(LogEventLevel.Warning);
        Property(logged, "Email").Should().Be(victim.Email);
    }

    [Fact]
    public async Task Locking_and_unlocking_are_both_logged_with_the_administrator_and_the_target()
    {
        ApplicationUser victim = await host.CreateUserAsync("log-lock-cycle@example.test", Password, Token);
        using HttpClient admin = await AdministratorClientAsync("log-cycle-admin@example.test");

        int mark = HostLogCapture.Instance.Mark();

        using (HttpResponseMessage locked = await PostAsync(
            admin, "/account/admin/set-lockout", ("email", victim.Email!), ("locked", "true")))
        {
            locked.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        using (HttpResponseMessage unlocked = await PostAsync(
            admin, "/account/admin/set-lockout", ("email", victim.Email!), ("locked", "false")))
        {
            unlocked.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        // Both edges. A log that records only the lock leaves "who gave this account access
        // back, and when" unanswerable -- and that is the question asked after a compromise.
        foreach (string name in new[] { "AdminLockedUser", "AdminUnlockedUser" })
        {
            LogEvent logged = SingleEvent(mark, name);

            Property(logged, "Actor").Should().Be("log-cycle-admin@example.test");
            Property(logged, "Target").Should().Be(victim.Email);
        }
    }

    [Fact]
    public async Task Creating_the_first_administrator_is_logged()
    {
        await host.ResetUsersAsync(Token);

        int mark = HostLogCapture.Instance.Mark();

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        {
            using HttpResponseMessage created = await PostAsync(
                client,
                "/account/setup",
                ("email", "log-first@example.test"),
                ("displayName", "First Administrator"),
                ("password", Password));

            created.Headers.Location?.OriginalString.Should().Be("/account/login");
        }

        LogEvent logged = SingleEvent(mark, "FirstAdministratorCreated");

        Property(logged, "Email").Should().Be("log-first@example.test");
    }

    [Fact]
    public async Task Every_operator_entrypoint_records_its_action()
    {
        // The command-line entrypoints bypass authentication, the rate limiter, the
        // enrolment gate and every permission policy, deliberately -- which is exactly why
        // they must not also bypass the log. They run as subprocesses, so the console sink's
        // output is the evidence rather than HostLogCapture.
        await host.ResetUsersAsync(Token);

        (int created, string createOutput) =
            await RunAsync(["--create-admin", "log-ops@example.test"], Password);
        created.Should().Be(0, createOutput);
        createOutput.Should().Contain("AuthEvent OperatorCreatedAdmin log-ops@example.test");

        (int reset, string resetOutput) =
            await RunAsync(["--reset-password", "log-ops@example.test"], ReplacementPassword);
        reset.Should().Be(0, resetOutput);
        resetOutput.Should().Contain("AuthEvent OperatorResetPassword log-ops@example.test");

        (int cleared, string clearOutput) =
            await RunAsync(["--reset-mfa", "log-ops@example.test"], standardInput: null);
        cleared.Should().Be(0, clearOutput);
        clearOutput.Should().Contain("AuthEvent OperatorResetMfa log-ops@example.test");

        (int unlocked, string unlockOutput) =
            await RunAsync(["--unlock-user", "log-ops@example.test"], standardInput: null);
        unlocked.Should().Be(0, unlockOutput);
        unlockOutput.Should().Contain("AuthEvent OperatorUnlockedUser log-ops@example.test");

        // The password came off standard input on two of those commands and must not have
        // come back out on either.
        foreach (string output in new[] { createOutput, resetOutput, clearOutput, unlockOutput })
        {
            output.Should().NotContain(Password);
            output.Should().NotContain(ReplacementPassword);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every rendered message and every property value, in one string. Both halves matter:
    /// a secret can reach a sink either as part of the message text or as a structured
    /// property nothing renders.
    /// </summary>
    private static string Flatten(IEnumerable<LogEvent> events) =>
        string.Join(
            Environment.NewLine,
            events.Select(logEvent => string.Join(
                " ",
                [
                    logEvent.RenderMessage(CultureInfo.InvariantCulture),
                    .. logEvent.Properties.Select(property =>
                        property.Value.ToString(null, CultureInfo.InvariantCulture)),
                    logEvent.Exception?.ToString() ?? string.Empty,
                ])));

    private static IEnumerable<LogEvent> AuthEventsIn(IEnumerable<LogEvent> events) =>
        events.Where(logEvent => logEvent.Properties.ContainsKey("Event"));

    private static string? Property(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value)
            ? value.ToString(null, CultureInfo.InvariantCulture).Trim('"')
            : null;

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        await client.GetAsync(new Uri(path, UriKind.Relative), Token);

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        params (string Name, string Value)[] fields)
    {
        using FormUrlEncodedContent form =
            new([.. fields.Select(field => new KeyValuePair<string, string>(field.Name, field.Value))]);

        return await client.PostAsync(new Uri(path, UriKind.Relative), form, Token);
    }

    /// <summary>The data-protected cookie the enrolment handler uses to carry the codes.</summary>
    private static string RecoveryCookie(HttpResponseMessage response)
    {
        const string Name = "fakturenn_recovery=";

        string header = (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? values : [])
            .SingleOrDefault(candidate => candidate.StartsWith(Name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Enrolment set no recovery cookie.");

        string value = header[Name.Length..];
        int end = value.IndexOf(';', StringComparison.Ordinal);

        return end < 0 ? value : value[..end];
    }

    /// <summary>
    /// The one event named <paramref name="name"/> written since <paramref name="mark"/>.
    /// Exactly one, not "at least one": a handler that logged the same event twice would be
    /// a bug an "any" assertion would hide.
    /// </summary>
    private static LogEvent SingleEvent(int mark, string name)
    {
        IReadOnlyList<LogEvent> written = HostLogCapture.Instance.Since(mark);

        return written
            .Where(logEvent => string.Equals(Property(logEvent, "Event"), name, StringComparison.Ordinal))
            .Should().ContainSingle($"exactly one {name} event must have been written")
            .Subject;
    }

    private HttpClient ClientFor(Cookie session)
    {
        CookieContainer cookies = new();
        cookies.Add(new Uri(host.BaseAddress), session);

        return host.CreateClient(cookies);
    }

    private async Task<HttpClient> AdministratorClientAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, Password, Token);
        await host.AssignAdministratorRoleAsync(user.Id, Token);
        string key = await host.EnableTwoFactorAsync(user.Id);

        HttpClient client = host.CreateClient(new CookieContainer());
        await SignInHelper.SignInAsync(client, user.UserName!, Password, key);

        return client;
    }

    private Task<(int ExitCode, string Output)> RunAsync(string[] arguments, string? standardInput) =>
        HostProcess.RunAsync(host.ConnectionString, arguments, standardInput, Token);
}
