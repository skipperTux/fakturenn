using System.Globalization;
using System.Net;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// Sign-in, the two-factor challenge, recovery-code sign-in, lockout and the forced
/// password change — all driven over HTTP through the real host.
/// <para>
/// These are the security properties of the epic, so none of them is asserted against a
/// <c>SignInManager</c> built for the test. Every code submitted here is a real RFC 6238
/// code or a real recovery code the host itself issued.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed class SignInTests(SetupHostFixture host)
{
    /// <summary>Satisfies the configured policy: twelve characters, upper, lower, digit.</summary>
    private const string Password = "Korrekt-Pferd-42";

    private const string ReplacementPassword = "Anderes-Pferd-77";

    /// <summary>
    /// A page carrying <c>[Authorize]</c>, used as the probe for "is this client actually
    /// signed in". Anything that renders it has an application cookie the pipeline accepts.
    /// </summary>
    private const string AuthorizedPage = "/account/enrol-totp";

    [Fact]
    public async Task A_correct_password_alone_does_not_produce_an_authenticated_session()
    {
        // The single most important assertion in this task. Knowing the password gets a
        // client to the challenge and no further: no application cookie is issued, and an
        // [Authorize] page is still closed to it.
        (ApplicationUser user, string _) = await EnrolledUserAsync("password-only@example.test");

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies, From("127.0.0.11"));

        using (HttpResponseMessage response = await PostPasswordAsync(client, user.UserName!, Password))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location?.OriginalString.Should().Be("/account/login-2fa");
        }

        CookieNames(cookies).Should().NotContain(
            host.ApplicationCookieName,
            "the password step may only issue the two-factor cookie, never the application cookie");

        using HttpResponseMessage probe = await GetAsync(client, AuthorizedPage);
        probe.StatusCode.Should().Be(HttpStatusCode.Found);
        probe.Headers.Location?.OriginalString.Should().Contain(
            "/account/login?ReturnUrl=",
            "an [Authorize] page must still challenge a client that has only passed the password step");
    }

    [Fact]
    public async Task The_second_factor_completes_the_sign_in()
    {
        // The positive control for the test above: without it, a sign-in endpoint that
        // never signs anybody in at all would satisfy "a password alone is not enough".
        (ApplicationUser user, string key) = await EnrolledUserAsync("both-factors@example.test");

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies, From("127.0.0.12"));

        await PasswordStepAsync(client, user.UserName!);

        using (HttpResponseMessage response = await PostCodeAsync(client, "/account/login-2fa/submit", CurrentCode(key)))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location?.OriginalString.Should().Be("/");
        }

        CookieNames(cookies).Should().Contain(host.ApplicationCookieName);

        using HttpResponseMessage probe = await GetAsync(client, AuthorizedPage);
        probe.StatusCode.Should().Be(HttpStatusCode.OK, "both factors were supplied");
    }

    [Fact]
    public async Task A_wrong_authenticator_code_does_not_complete_the_sign_in()
    {
        (ApplicationUser user, string key) = await EnrolledUserAsync("wrong-code@example.test");

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies, From("127.0.0.13"));

        await PasswordStepAsync(client, user.UserName!);

        using (HttpResponseMessage response = await PostCodeAsync(client, "/account/login-2fa/submit", WrongCode(key)))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location?.OriginalString.Should().Be("/account/login-2fa?error=invalid");
        }

        CookieNames(cookies).Should().NotContain(host.ApplicationCookieName);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_is_then_spent()
    {
        (ApplicationUser user, string _) = await EnrolledUserAsync("recovery@example.test");
        string[] codes = await host.GenerateRecoveryCodesAsync(user.Id, count: 10);
        string spent = codes[0];

        CookieContainer first = new();
        using (HttpClient client = host.CreateClient(first, From("127.0.0.14")))
        {
            await PasswordStepAsync(client, user.UserName!);

            using HttpResponseMessage response =
                await PostCodeAsync(client, "/account/login-recovery/submit", spent);

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location?.OriginalString.Should().Be("/");
        }

        CookieNames(first).Should().Contain(host.ApplicationCookieName);

        (await host.CountRecoveryCodesAsync(user.Id)).Should().Be(
            codes.Length - 1,
            "redeeming a code must remove it from the account, not merely compare against it");

        // A second, independent sign-in offering the same code. Anything that accepts it
        // has turned a one-shot credential into a reusable password.
        CookieContainer second = new();
        using (HttpClient client = host.CreateClient(second, From("127.0.0.14")))
        {
            await PasswordStepAsync(client, user.UserName!);

            using HttpResponseMessage response =
                await PostCodeAsync(client, "/account/login-recovery/submit", spent);

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location?.OriginalString.Should().Be("/account/login-recovery?error=invalid");
        }

        CookieNames(second).Should().NotContain(host.ApplicationCookieName);
    }

    [Fact]
    public async Task Failed_password_attempts_lock_the_account_and_the_lock_holds_against_the_right_password()
    {
        // Spec section 8: five failures, fifteen-minute window. The fifth failure is the
        // one that locks -- AccessFailedAsync increments to the limit and SignInManager
        // reports LockedOut for that same attempt rather than the next one.
        (ApplicationUser user, string _) = await EnrolledUserAsync("lockout@example.test");

        using HttpClient client = host.CreateClient(new CookieContainer(), From("127.0.0.15"));

        for (int attempt = 1; attempt < 5; attempt++)
        {
            using HttpResponseMessage response =
                await PostPasswordAsync(client, user.UserName!, "Falsch-Pferd-99");

            response.Headers.Location?.OriginalString.Should().Be(
                "/account/login?error=invalid",
                $"attempt {attempt} is below the limit");
        }

        using (HttpResponseMessage fifth = await PostPasswordAsync(client, user.UserName!, "Falsch-Pferd-99"))
        {
            fifth.StatusCode.Should().Be(HttpStatusCode.Found);
            fifth.Headers.Location?.OriginalString.Should().Be("/account/lockout");
        }

        // The durable half. A lock that the correct password walks through is not a lock.
        using (HttpResponseMessage correct = await PostPasswordAsync(client, user.UserName!, Password))
        {
            correct.Headers.Location?.OriginalString.Should().Be("/account/lockout");
        }

        await using IdentityDbContext context = host.CreateIdentityContext();
        ApplicationUser stored = await context.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken);

        stored.LockoutEnd.Should().NotBeNull("lockout is a database column, so it survives a restart");
        stored.LockoutEnd!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
        stored.AccessFailedCount.Should().Be(
            0,
            "AccessFailedAsync zeroes the counter in the same call that sets LockoutEnd, so the "
            + "column is not the record of how many attempts were made");
    }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_password_are_indistinguishable()
    {
        // Lockout plus a distinguishable failure would make this endpoint an account
        // oracle: an attacker could enumerate valid addresses without ever guessing a
        // password. Compared byte for byte rather than asserted to be "similar".
        (ApplicationUser known, string _) = await EnrolledUserAsync("known@example.test");

        using HttpClient client = host.CreateClient(new CookieContainer(), From("127.0.0.16"));

        using HttpResponseMessage unknown =
            await PostPasswordAsync(client, "no-such-account@example.test", Password);
        string unknownBody = await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using HttpResponseMessage wrongPassword =
            await PostPasswordAsync(client, known.UserName!, "Falsch-Pferd-99");
        string wrongPasswordBody =
            await wrongPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        wrongPassword.StatusCode.Should().Be(unknown.StatusCode);
        wrongPassword.Headers.Location?.OriginalString.Should()
            .Be(unknown.Headers.Location?.OriginalString);
        wrongPasswordBody.Should().Be(unknownBody);
    }

    [Fact]
    public async Task A_password_somebody_else_chose_sends_the_user_to_change_it()
    {
        (ApplicationUser user, string key) = await EnrolledUserAsync("must-change@example.test");
        await host.SetMustChangePasswordAsync(user.Id, value: true, TestContext.Current.CancellationToken);

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies, From("127.0.0.17"));

        await PasswordStepAsync(client, user.UserName!);

        using HttpResponseMessage response =
            await PostCodeAsync(client, "/account/login-2fa/submit", CurrentCode(key));

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be(
            "/account/change-password",
            "a credential somebody else chose stops being shared the first time it is used");
    }

    [Fact]
    public async Task A_rejected_password_change_leaves_the_flag_and_the_old_password_in_place()
    {
        (ApplicationUser user, string key) = await EnrolledUserAsync("bad-change@example.test");
        await host.SetMustChangePasswordAsync(user.Id, value: true, TestContext.Current.CancellationToken);

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies, From("127.0.0.18"));
        await SignInAsync(client, user.UserName!, key);

        using (HttpResponseMessage wrongCurrent =
            await PostChangePasswordAsync(client, "Falsch-Pferd-99", ReplacementPassword))
        {
            wrongCurrent.Headers.Location?.OriginalString.Should().StartWith("/account/change-password?error=");
        }

        using (HttpResponseMessage weakReplacement =
            await PostChangePasswordAsync(client, Password, "short"))
        {
            weakReplacement.Headers.Location?.OriginalString.Should().StartWith("/account/change-password?error=");
        }

        await using IdentityDbContext context = host.CreateIdentityContext();
        ApplicationUser stored = await context.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken);

        stored.MustChangePassword.Should().BeTrue("only a successful change clears the flag");
    }

    [Fact]
    public async Task A_successful_password_change_clears_the_flag_and_keeps_the_session_alive()
    {
        (ApplicationUser user, string key) = await EnrolledUserAsync("good-change@example.test");
        await host.SetMustChangePasswordAsync(user.Id, value: true, TestContext.Current.CancellationToken);

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies, From("127.0.0.19"));
        await SignInAsync(client, user.UserName!, key);

        using (HttpResponseMessage changed =
            await PostChangePasswordAsync(client, Password, ReplacementPassword))
        {
            changed.StatusCode.Should().Be(HttpStatusCode.Found);
            changed.Headers.Location?.OriginalString.Should().Be("/");
        }

        await using (IdentityDbContext context = host.CreateIdentityContext())
        {
            ApplicationUser stored = await context.Users.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken);

            stored.MustChangePassword.Should().BeFalse();
        }

        // ChangePasswordAsync rotates the security stamp, and the validator revalidates
        // after a minute. Without RefreshSignInAsync the cookie would still carry the old
        // stamp and this session would die shortly after succeeding.
        string stamp = await host.ReadSecurityStampAsync(user.Id, TestContext.Current.CancellationToken);
        StampClaim(cookies).Should().Be(stamp);

        using HttpResponseMessage probe = await GetAsync(client, AuthorizedPage);
        probe.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        (ApplicationUser user, string key) = await EnrolledUserAsync("logout@example.test");

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies, From("127.0.0.20"));
        await SignInAsync(client, user.UserName!, key);

        using (HttpResponseMessage response = await client.PostAsync(
            new Uri("/account/logout", UriKind.Relative),
            new FormUrlEncodedContent([]),
            TestContext.Current.CancellationToken))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location?.OriginalString.Should().Be("/account/login");
        }

        using HttpResponseMessage probe = await GetAsync(client, AuthorizedPage);
        probe.StatusCode.Should().Be(HttpStatusCode.Found, "the session must be gone, not merely redirected away from");
    }

    /// <summary>
    /// The loopback address a test posts from. Every test uses a different one because the
    /// account rate limiter partitions on the client address plus the <c>email</c> form
    /// field, and the second-factor, change-password and sign-out forms carry no e-mail —
    /// so those endpoints share one ten-per-minute budget per address. Sharing an address
    /// across the class makes later tests answer 429 for a reason that has nothing to do
    /// with what they assert. Measured, not assumed: six of these tests failed that way
    /// before they were separated.
    /// </summary>
    private static IPAddress From(string address) => IPAddress.Parse(address);

    private static string CurrentCode(string key) =>
        new Totp(Base32Encoding.ToBytes(key)).ComputeTotp();

    /// <summary>
    /// A syntactically valid six-digit code that is not the current one. Derived from the
    /// real code so it cannot collide with it, including across a window roll.
    /// </summary>
    private static string WrongCode(string key) =>
        ((int.Parse(CurrentCode(key), CultureInfo.InvariantCulture) + 500000) % 1000000)
            .ToString("D6", CultureInfo.InvariantCulture);

    private static IEnumerable<string> CookieNames(CookieContainer cookies) =>
        cookies.GetAllCookies()
            .Where(cookie => !cookie.Expired)
            .Select(cookie => cookie.Name);

    private static async Task<HttpResponseMessage> PostPasswordAsync(
        HttpClient client,
        string email,
        string password)
    {
        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("email", email),
            new KeyValuePair<string, string>("password", password),
        ]);

        return await client.PostAsync(
            new Uri("/account/login/submit", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string path, string code)
    {
        using FormUrlEncodedContent form = new([new KeyValuePair<string, string>("code", code)]);

        return await client.PostAsync(
            new Uri(path, UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostChangePasswordAsync(
        HttpClient client,
        string current,
        string replacement)
    {
        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("currentPassword", current),
            new KeyValuePair<string, string>("newPassword", replacement),
        ]);

        return await client.PostAsync(
            new Uri("/account/change-password/submit", UriKind.Relative),
            form,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    private static async Task PasswordStepAsync(HttpClient client, string email)
    {
        using HttpResponseMessage response = await PostPasswordAsync(client, email, Password);

        response.Headers.Location?.OriginalString.Should().Be(
            "/account/login-2fa", "the password step must reach the challenge before a test can go further");
    }

    private static async Task SignInAsync(HttpClient client, string email, string key)
    {
        await PasswordStepAsync(client, email);

        using HttpResponseMessage response =
            await PostCodeAsync(client, "/account/login-2fa/submit", CurrentCode(key));

        response.Headers.Location?.OriginalString.Should().NotBe("/account/login-2fa?error=invalid");
    }

    private string? StampClaim(CookieContainer cookies)
    {
        Cookie cookie = cookies.GetAllCookies()
            .Single(candidate => candidate.Name == host.ApplicationCookieName && !candidate.Expired);

        return host.ReadSecurityStampClaim(cookie.Value);
    }

    private async Task<(ApplicationUser User, string Key)> EnrolledUserAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, Password, TestContext.Current.CancellationToken);
        string key = await host.EnableTwoFactorAsync(user.Id);

        return (user, key);
    }
}
