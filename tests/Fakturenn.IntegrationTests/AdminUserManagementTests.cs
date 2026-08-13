using System.Net;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// Administrator user management, driven over HTTP through the real host.
/// <para>
/// Every assertion here is about the pipeline rather than about <c>UserManager</c>: that
/// the endpoints are mapped, that authorization is attached to them, and that the state
/// they write is the state the rest of the application reads. A test that called
/// <c>UserManager</c> directly would pass while the endpoints were unauthorised.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed class AdminUserManagementTests(SetupHostFixture host)
{
    /// <summary>Satisfies the configured policy: twelve characters, upper, lower, digit.</summary>
    private const string Password = "Korrekt-Pferd-42";

    /// <summary>A second policy-satisfying password, for the accounts these tests create.</summary>
    private const string OtherPassword = "Anderes-Pferd-77";

    private const string CreateUserPath = "/account/admin/create-user";

    private const string ResetPasswordPath = "/account/admin/reset-password";

    private const string ClearMfaPath = "/account/admin/clear-mfa";

    private const string SetLockoutPath = "/account/admin/set-lockout";

    /// <summary>
    /// A page that needs nothing but an authenticated user, so a 302 away from it means the
    /// session ended rather than that a permission was missing.
    /// <para>
    /// It is also on the enrolment gate's allowlist, which is what lets a user who has not
    /// enrolled TOTP serve as the subject of a session test without the gate answering
    /// first.
    /// </para>
    /// </summary>
    private const string SessionProbe = "/account/change-password";

    [Theory]
    [InlineData(CreateUserPath)]
    [InlineData(ResetPasswordPath)]
    [InlineData(ClearMfaPath)]
    [InlineData(SetLockoutPath)]
    public async Task Every_administration_endpoint_refuses_a_user_without_users_manage(string path)
    {
        using HttpClient client = await SignedInClientAsync($"admin-forbidden-{Slug(path)}@example.test");

        using HttpResponseMessage response = await PostAsync(
            client, path, ("email", "somebody@example.test"));

        // Not a bare 403: ConfigureApplicationCookie sets AccessDeniedPath, so the cookie
        // handler turns the forbid into a redirect to that page. The distinction that
        // matters is the one this asserts -- an authenticated caller without the permission
        // never reaches the handler.
        response.StatusCode.Should().Be(HttpStatusCode.Found);
        LocationPath(response).Should().Be(
            "/account/denied", $"POST {path} must require {Permissions.UsersManage}");
    }

    [Fact]
    public async Task An_administrator_reaches_every_administration_endpoint()
    {
        // The positive control for the theory above. Without it, an endpoint that refused
        // everybody -- or one that was never mapped at all -- would satisfy every case.
        //
        // The post carries a valid antiforgery token, which it did not have to before: this
        // request used to succeed with a session cookie and nothing else, and that is the
        // hole the token now closes. The paired negative is the test below.
        using HttpClient client = await AdministratorClientAsync("admin-allowed@example.test");
        ApplicationUser subject =
            await host.CreateUserAsync("admin-allowed-subject@example.test", Password, Token);

        using HttpResponseMessage response = await PostAsync(
            client, ClearMfaPath, ("email", subject.Email!));

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be("/admin/users");
    }

    [Theory]
    [InlineData(CreateUserPath)]
    [InlineData(ResetPasswordPath)]
    [InlineData(ClearMfaPath)]
    [InlineData(SetLockoutPath)]
    public async Task Every_administration_endpoint_refuses_a_post_without_an_antiforgery_token(string path)
    {
        // A session cookie alone must not be enough. These endpoints are reached from
        // /admin/users, whose forms have always rendered a token -- nothing on the server
        // looked at it, because a handler that reads the form by hand gets no antiforgery
        // metadata inferred for it and UseAntiforgery skips what it finds no metadata on.
        //
        // The caller here is a full administrator, so authorization cannot be what refuses
        // the request: the only thing missing is the token.
        using HttpClient client = await AdministratorClientAsync($"admin-forged-{Slug(path)}@example.test");
        ApplicationUser subject =
            await host.CreateUserAsync($"admin-forged-subject-{Slug(path)}@example.test", Password, Token);

        using HttpResponseMessage response = await AntiforgeryHelper.PostWithoutTokenAsync(
            client, path, ("email", subject.Email!), ("locked", "true"));

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest,
            $"POST {path} must validate the antiforgery token the form already renders");

        ApplicationUser unchanged = await ReadUserAsync(subject.Email!);
        unchanged.LockoutEnd.Should().BeNull("a refused post must change nothing");
        unchanged.MustChangePassword.Should().BeFalse("a refused post must change nothing");
    }

    [Fact]
    public async Task The_user_list_refuses_a_user_without_users_read()
    {
        using HttpClient client = await SignedInClientAsync("list-forbidden@example.test");

        using HttpResponseMessage response = await GetAsync(client, "/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        LocationPath(response).Should().Be("/account/denied");
    }

    [Fact]
    public async Task The_user_list_renders_for_an_administrator()
    {
        using HttpClient client = await AdministratorClientAsync("list-allowed@example.test");

        using HttpResponseMessage response = await GetAsync(client, "/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("list-allowed@example.test");
    }

    [Fact]
    public async Task An_administrator_created_user_owes_both_a_second_factor_and_a_password_of_their_own()
    {
        using HttpClient client = await AdministratorClientAsync("create-admin@example.test");

        using (HttpResponseMessage response = await PostAsync(
            client,
            CreateUserPath,
            ("email", "created@example.test"),
            ("displayName", "Created User"),
            ("password", OtherPassword)))
        {
            response.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        ApplicationUser created = await ReadUserAsync("created@example.test");

        created.MustEnrolTotp.Should().BeTrue("the account has no second factor yet");
        created.MustChangePassword.Should().BeTrue(
            "the administrator chose this password, so it is shared until the user replaces it");
    }

    [Fact]
    public async Task An_administrator_created_user_is_sent_to_enrolment_by_the_gate()
    {
        // The flags above are only worth setting because something acts on them. This is
        // the Task 12 gate doing so, on the account the endpoint above actually produced.
        using HttpClient admin = await AdministratorClientAsync("create-gate-admin@example.test");

        using (HttpResponseMessage response = await PostAsync(
            admin,
            CreateUserPath,
            ("email", "created-gated@example.test"),
            ("displayName", "Created Gated"),
            ("password", OtherPassword)))
        {
            response.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        using HttpClient created = host.CreateClient(new CookieContainer());
        using (HttpResponseMessage signIn =
            await SignInHelper.PostPasswordAsync(created, "created-gated@example.test", OtherPassword))
        {
            signIn.Headers.Location?.OriginalString.Should().Be(
                "/", "the account has no second factor yet, so the password step signs it in");
        }

        using HttpResponseMessage application = await GetAsync(created, "/");

        application.Headers.Location?.OriginalString.Should().Be(
            "/account/enrol-totp",
            "enrolment comes before the forced password change, so the new password is chosen by an account with two factors");
    }

    [Fact]
    public async Task Locking_a_user_ends_their_existing_session()
    {
        // THE test for this task. Identity rotates the security stamp on a password reset
        // and on a two-factor change but NOT on lockout, so without an explicit rotation in
        // the endpoint a locked user's cookie keeps working for as long as it lives. A lock
        // that leaves a working session is not a lock.
        ApplicationUser victim = await host.CreateUserAsync("lock-victim@example.test", Password, Token);

        // Issued two minutes ago, which is the only artificial thing here. The
        // security-stamp validator revalidates only once
        // SecurityStampValidatorOptions.ValidationInterval (one minute) has elapsed since
        // the ticket was issued, so a cookie minted "now" would sail through the next
        // request no matter what the endpoint did. This is the state a real session reaches
        // one minute after sign-in.
        Cookie session = await host.CreateAuthenticationCookieAsync(
            victim, DateTimeOffset.UtcNow.AddMinutes(-2));

        using (HttpClient before = ClientWith(session))
        {
            using HttpResponseMessage reachable = await GetAsync(before, SessionProbe);

            reachable.StatusCode.Should().Be(
                HttpStatusCode.OK, "the session has to work before locking can be shown to end it");
        }

        using HttpClient admin = await AdministratorClientAsync("lock-admin@example.test");
        using (HttpResponseMessage locked = await PostAsync(
            admin, SetLockoutPath, ("email", victim.Email!), ("locked", "true")))
        {
            locked.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        // A fresh jar holding the same ticket, deliberately. A successful revalidation sets
        // ShouldRenew, so the handler reissues the cookie with a current IssuedUtc -- reuse
        // one container and the probe above would hand this request a freshly issued ticket
        // that skips revalidation entirely, and the test would fail with correct code.
        using HttpClient after = ClientWith(session);
        using HttpResponseMessage bounced = await GetAsync(after, SessionProbe);

        bounced.StatusCode.Should().Be(HttpStatusCode.Found);
        LocationPath(bounced).Should().Be(
            "/account/login", "locking a user must end the session they already hold");
    }

    [Fact]
    public async Task Locking_a_user_stops_them_signing_in_again_and_unlocking_lets_them_back()
    {
        ApplicationUser user = await host.CreateUserAsync("lock-cycle@example.test", Password, Token);
        string key = await host.EnableTwoFactorAsync(user.Id);

        using HttpClient admin = await AdministratorClientAsync("lock-cycle-admin@example.test");

        using (HttpResponseMessage locked = await PostAsync(
            admin, SetLockoutPath, ("email", user.Email!), ("locked", "true")))
        {
            locked.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        using (HttpResponseMessage refused = await SignInHelper.PostPasswordAsync(client, user.UserName!, Password))
        {
            refused.Headers.Location?.OriginalString.Should().Be(
                "/account/lockout", "a locked account cannot sign in, correct password or not");
        }

        using (HttpResponseMessage unlocked = await PostAsync(
            admin, SetLockoutPath, ("email", user.Email!), ("locked", "false")))
        {
            unlocked.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        using HttpClient recovered = host.CreateClient(new CookieContainer());
        await SignInHelper.SignInAsync(recovered, user.UserName!, Password, key);

        using HttpResponseMessage application = await GetAsync(recovered, "/");
        application.StatusCode.Should().Be(HttpStatusCode.OK, "unlocking must restore the account");
    }

    [Fact]
    public async Task Resetting_a_password_clears_the_lockout_and_forces_the_user_to_choose_their_own()
    {
        // A reset that left the account locked would be a surprise: the administrator has
        // just handed the user a password they cannot use.
        ApplicationUser user = await host.CreateUserAsync("reset-locked@example.test", Password, Token);
        string key = await host.EnableTwoFactorAsync(user.Id);

        using HttpClient admin = await AdministratorClientAsync("reset-admin@example.test");

        using (HttpResponseMessage locked = await PostAsync(
            admin, SetLockoutPath, ("email", user.Email!), ("locked", "true")))
        {
            locked.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        using (HttpResponseMessage reset = await PostAsync(
            admin, ResetPasswordPath, ("email", user.Email!), ("password", OtherPassword)))
        {
            reset.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        ApplicationUser stored = await ReadUserAsync(user.Email!);
        stored.LockoutEnd.Should().BeNull("a password reset lifts the lock it would otherwise contradict");
        stored.MustChangePassword.Should().BeTrue("the administrator knows this password");

        using HttpClient client = host.CreateClient(new CookieContainer());
        await SignInHelper.SignInAsync(client, user.UserName!, OtherPassword, key);

        using HttpResponseMessage application = await GetAsync(client, "/");
        application.Headers.Location?.OriginalString.Should().Be(
            "/account/change-password", "the reset password is shared until the user replaces it");
    }

    [Fact]
    public async Task Clearing_two_factor_forces_re_enrolment_and_replaces_the_recovery_codes()
    {
        // E02a has no recovery-code regeneration page on purpose: the recovery path is an
        // administrator clearing two-factor setup. That only works if re-enrolment issues a
        // fresh set -- if the old codes survived, a user who lost them would be no better
        // off and whoever found them would be no worse.
        ApplicationUser user = await host.CreateUserAsync("clear-mfa@example.test", Password, Token);
        await host.EnableTwoFactorAsync(user.Id);
        string[] original = await host.GenerateRecoveryCodesAsync(user.Id, 10);

        using (HttpClient client = host.CreateClient(new CookieContainer()))
        {
            using HttpResponseMessage passwordStep =
                await SignInHelper.PostPasswordAsync(client, user.UserName!, Password);
            passwordStep.Headers.Location?.OriginalString.Should().Be("/account/login-2fa");

            using HttpResponseMessage redeemed =
                await SignInHelper.PostCodeAsync(client, "/account/login-recovery/submit", original[0]);
            redeemed.Headers.Location?.OriginalString.Should().Be(
                "/", "the original codes have to work, or their replacement proves nothing");
        }

        (await host.CountRecoveryCodesAsync(user.Id)).Should().Be(9, "one original code was spent");

        using HttpClient admin = await AdministratorClientAsync("clear-mfa-admin@example.test");
        using (HttpResponseMessage cleared = await PostAsync(admin, ClearMfaPath, ("email", user.Email!)))
        {
            cleared.Headers.Location?.OriginalString.Should().Be("/admin/users");
        }

        ApplicationUser afterClearing = await ReadUserAsync(user.Email!);
        afterClearing.TwoFactorEnabled.Should().BeFalse();
        afterClearing.MustEnrolTotp.Should().BeTrue("the gate is what actually forces the re-enrolment");

        // Neither SetTwoFactorEnabledAsync nor ResetAuthenticatorKeyAsync touches the
        // recovery codes: without the endpoint's explicit call this reads 9, measured. They
        // are unreachable in that state, but an administrator clearing two-factor is often
        // doing it because the old factors may be compromised, and "unreachable for now"
        // is a weaker promise than "gone".
        (await host.CountRecoveryCodesAsync(user.Id)).Should().Be(
            0, "clearing two-factor must leave the account no second factor of any kind");

        // Re-enrol through the real endpoint, so the codes come from the handler that issues
        // them rather than from a test helper.
        using HttpClient enrolling = host.CreateClient(new CookieContainer());
        using (HttpResponseMessage signIn =
            await SignInHelper.PostPasswordAsync(enrolling, user.UserName!, Password))
        {
            signIn.Headers.Location?.OriginalString.Should().Be(
                "/", "clearing two-factor leaves the password as the only factor until re-enrolment");
        }

        // The enrolment page refuses a user who has already enrolled, so this is also the
        // assertion that clearing two-factor RE-OPENS it. Without the flag going back on,
        // the documented recovery path would end at a page the user is bounced off.
        using (HttpResponseMessage enrolment = await GetAsync(enrolling, "/account/enrol-totp"))
        {
            enrolment.StatusCode.Should().Be(
                HttpStatusCode.OK, "clearing two-factor must let the user enrol again");
        }

        string replacementKey = await host.ReadAuthenticatorKeyAsync(user.Id);
        replacementKey.Should().NotBeNullOrEmpty();

        using (HttpResponseMessage enrolled = await SignInHelper.PostCodeAsync(
            enrolling, "/account/enrol-totp/verify", SignInHelper.CurrentCode(replacementKey)))
        {
            enrolled.Headers.Location?.OriginalString.Should().Be("/account/recovery-codes");
        }

        (await host.CountRecoveryCodesAsync(user.Id)).Should().Be(
            10, "a fresh set replaces the remaining originals rather than adding to them");

        using (HttpResponseMessage page = await GetAsync(enrolling, "/account/recovery-codes"))
        {
            string html = await page.Content.ReadAsStringAsync(Token);

            foreach (string code in original)
            {
                html.Should().NotContain(code, "the replacement set must not be the old one");
            }
        }

        // The decisive check: an original code that was never spent is now refused by the
        // endpoint that redeems them.
        using HttpClient stale = host.CreateClient(new CookieContainer());
        using (HttpResponseMessage passwordStep =
            await SignInHelper.PostPasswordAsync(stale, user.UserName!, Password))
        {
            passwordStep.Headers.Location?.OriginalString.Should().Be(
                "/account/login-2fa", "the account has a second factor again");
        }

        using HttpResponseMessage refused =
            await SignInHelper.PostCodeAsync(stale, "/account/login-recovery/submit", original[1]);

        refused.Headers.Location?.OriginalString.Should().Be(
            "/account/login-recovery?error=invalid", "an unspent original code must not survive re-enrolment");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A distinct, readable user name per theory case.</summary>
    private static string Slug(string path) => new([.. path.Where(char.IsLetterOrDigit)]);

    /// <summary>
    /// The path a redirect points at, without the query.
    /// <para>
    /// The two kinds of redirect in this file do not agree on shape. The handlers'
    /// <c>Results.Redirect("/admin/users")</c> emits a relative location; the cookie
    /// handler's challenge and forbid emit an <b>absolute</b> one with a
    /// <c>ReturnUrl</c> appended. Comparing raw strings would make an assertion about the
    /// framework's URL formatting rather than about where the caller was sent.
    /// </para>
    /// </summary>
    private static string LocationPath(HttpResponseMessage response)
    {
        Uri location = response.Headers.Location
            ?? throw new InvalidOperationException("A redirect with no Location header.");

        return location.IsAbsoluteUri
            ? location.AbsolutePath
            : location.OriginalString.Split('?')[0];
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        await client.GetAsync(new Uri(path, UriKind.Relative), Token);

    /// <summary>
    /// A post carrying a valid antiforgery token, which every <c>/account</c> endpoint now
    /// requires.
    /// <para>
    /// The token comes from <c>/account/change-password</c> rather than from
    /// <c>/admin/users</c> — the page these forms actually live on — because half the tests
    /// here drive a user who is refused <c>/admin/users</c> on purpose. A token is bound to
    /// the caller, not to a form's action, so the page that issues it is free.
    /// </para>
    /// </summary>
    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        params (string Name, string Value)[] fields) =>
        await AntiforgeryHelper.PostAsync(client, AntiforgeryHelper.SignedInTokenPage, path, fields);

    private HttpClient ClientWith(Cookie session)
    {
        CookieContainer cookies = new();
        cookies.Add(new Uri(host.BaseAddress), session);

        return host.CreateClient(cookies);
    }

    /// <summary>A signed-in client for a fully enrolled user holding no role at all.</summary>
    private async Task<HttpClient> SignedInClientAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, Password, Token);
        string key = await host.EnableTwoFactorAsync(user.Id);

        HttpClient client = host.CreateClient(new CookieContainer());
        await SignInHelper.SignInAsync(client, user.UserName!, Password, key);

        return client;
    }

    /// <summary>
    /// A signed-in client for a user in the Administrator role. The role is assigned before
    /// the sign-in because permissions are claims minted into the cookie: assigning it
    /// afterwards would leave the client holding a principal with no permissions.
    /// </summary>
    private async Task<HttpClient> AdministratorClientAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, Password, Token);
        await host.AssignAdministratorRoleAsync(user.Id, Token);
        string key = await host.EnableTwoFactorAsync(user.Id);

        HttpClient client = host.CreateClient(new CookieContainer());
        await SignInHelper.SignInAsync(client, user.UserName!, Password, key);

        return client;
    }

    private async Task<ApplicationUser> ReadUserAsync(string email)
    {
        await using IdentityDbContext context = host.CreateIdentityContext();

        return await context.Users.AsNoTracking().SingleAsync(user => user.Email == email, Token);
    }
}
