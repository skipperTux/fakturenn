using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OtpNet;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// TOTP enrolment, driven over HTTP through the real host.
/// <para>
/// Every code these tests submit is computed with <c>Otp.NET</c> from the key the
/// enrolment page actually displayed, so the real
/// <c>UserManager.VerifyTwoFactorTokenAsync</c> runs against real RFC 6238. Nothing here
/// stubs the verifier: a stub would prove the endpoint calls something, not that the
/// something rejects a wrong code.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed partial class EnrolTotpTests(SetupHostFixture host)
{
    private const string RecoveryCookieName = "fakturenn_recovery";

    private const int ExpectedRecoveryCodes = 10;

    /// <summary>
    /// Satisfies the configured policy. Only the already-enrolled users need one: the tests
    /// about re-enrolment sign in again over HTTP to redeem a recovery code.
    /// </summary>
    private const string EnrolledPassword = "Korrekt-Pferd-42";

    [Fact]
    public async Task Enrolling_with_a_valid_code_stores_both_second_factors_as_ciphertext()
    {
        // Task 6 proved the value converter with a token inserted by hand. This is the
        // real path: UserManager writes the authenticator key on enrolment and the
        // recovery codes on generation, and neither may reach the column readable.
        (ApplicationUser user, CookieContainer cookies) =
            await SignedInUserAsync("ciphertext@example.test");

        using HttpClient client = host.CreateClient(cookies);
        string key = await ReadAuthenticatorKeyAsync(client);

        using (HttpResponseMessage posted = await PostCodeAsync(client, CurrentCode(key)))
        {
            posted.StatusCode.Should().Be(HttpStatusCode.Found);
            posted.Headers.Location?.OriginalString.Should().Be("/account/recovery-codes");
        }

        string[] codes = await ReadRecoveryCodesAsync(client);
        codes.Should().HaveCount(ExpectedRecoveryCodes);

        string storedKey = await ReadTokenValueAsync(user.Id, "AuthenticatorKey");
        storedKey.Should().NotContain(key, "the shared secret must not be readable in the column");

        string storedCodes = await ReadTokenValueAsync(user.Id, "RecoveryCodes");
        foreach (string code in codes)
        {
            storedCodes.Should().NotContain(code, "a recovery code must not be readable in the column");
        }

        await using IdentityDbContext context = host.CreateIdentityContext();
        ApplicationUser stored = await context.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken);

        stored.TwoFactorEnabled.Should().BeTrue();
        stored.MustEnrolTotp.Should().BeFalse("a verified code is what clears the enrolment gate");
    }

    [Fact]
    public async Task Enrolment_refreshes_the_session_so_the_stamp_rotation_does_not_sign_the_user_out()
    {
        // SetTwoFactorEnabledAsync rotates the security stamp, and Task 7B deliberately
        // set SecurityStampValidatorOptions.ValidationInterval to one minute so a locked
        // user cannot keep a working session for half an hour. Together those would sign
        // a user out about a minute after they finished enrolling.
        //
        // The mechanism is a claim: the cookie carries the stamp it was issued under, and
        // the validator signs the session out when that claim no longer matches the stored
        // stamp. Asserting on the claim proves the property now instead of waiting for the
        // validator to act on it.
        (ApplicationUser user, CookieContainer cookies) = await SignedInUserAsync("refresh@example.test");

        using HttpClient client = host.CreateClient(cookies);
        string key = await ReadAuthenticatorKeyAsync(client);

        string before = await host.ReadSecurityStampAsync(user.Id, TestContext.Current.CancellationToken);

        using (HttpResponseMessage posted = await PostCodeAsync(client, CurrentCode(key)))
        {
            posted.StatusCode.Should().Be(HttpStatusCode.Found);
        }

        string after = await host.ReadSecurityStampAsync(user.Id, TestContext.Current.CancellationToken);
        after.Should().NotBe(
            before,
            "enabling two-factor authentication rotates the stamp -- without that rotation this test would prove nothing");

        Cookie session = cookies.GetAllCookies()
            .Single(candidate => candidate.Name == host.ApplicationCookieName && !candidate.Expired);

        host.ReadSecurityStampClaim(session.Value).Should().Be(
            after,
            "the session must be re-issued under the new stamp, or the validator ends it within the minute");
    }

    [Fact]
    public async Task The_recovery_codes_reach_the_browser_only_as_ciphertext()
    {
        (_, CookieContainer cookies) = await SignedInUserAsync("cookie@example.test");

        using HttpClient client = host.CreateClient(cookies);
        string key = await ReadAuthenticatorKeyAsync(client);

        string cookieValue;
        using (HttpResponseMessage posted = await PostCodeAsync(client, CurrentCode(key)))
        {
            cookieValue = SetCookieValue(posted, RecoveryCookieName);
        }

        // Structural, and checked before the codes are known: the codes are joined with
        // ';' before protection, so a semicolon surviving into the cookie means the join
        // was written out rather than the ciphertext. Data Protection payloads are
        // base64url, whose alphabet has no ';', so this cannot false-positive.
        string decoded = Uri.UnescapeDataString(cookieValue);
        decoded.Should().NotContain(
            ";",
            $"the stash must be one protected blob, not a delimited list, but it was: {decoded}");

        string[] codes = await ReadRecoveryCodesAsync(client);
        codes.Should().HaveCount(ExpectedRecoveryCodes);

        foreach (string code in codes)
        {
            decoded.Should().NotContain(code);
        }
    }

    [Fact]
    public async Task The_recovery_codes_are_displayed_exactly_once()
    {
        (ApplicationUser user, CookieContainer cookies) =
            await SignedInUserAsync("showonce@example.test");

        using HttpClient client = host.CreateClient(cookies);
        string key = await ReadAuthenticatorKeyAsync(client);

        using (HttpResponseMessage posted = await PostCodeAsync(client, CurrentCode(key)))
        {
            posted.StatusCode.Should().Be(HttpStatusCode.Found);
        }

        string[] first = await ReadRecoveryCodesAsync(client);
        first.Should().HaveCount(ExpectedRecoveryCodes);

        // The cookie store honours the server's deletion, so this is the same request a
        // browser would make on a reload or a back-navigation.
        string second = await GetAsync(client, "/account/recovery-codes");
        ExtractRecoveryCodes(second).Should().BeEmpty();
        second.Should().Contain("data-testid=\"recovery-empty\"");

        // The hazard this behaviour creates, stated as an assertion rather than a
        // comment: the account holds ten codes the user may never have written down.
        // There is no regeneration page in E02a; recovery is --reset-mfa or an
        // administrator clearing TOTP, which forces re-enrolment and a fresh set.
        int held = await host.CountRecoveryCodesAsync(user.Id);
        held.Should().Be(ExpectedRecoveryCodes);
    }

    [Fact]
    public async Task A_tampered_recovery_cookie_yields_no_codes_and_no_error()
    {
        (_, CookieContainer cookies) = await SignedInUserAsync("tampered@example.test");
        cookies.Add(new Uri(host.BaseAddress), new Cookie(RecoveryCookieName, "not-a-protected-payload", "/"));

        using HttpClient client = host.CreateClient(cookies);
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/account/recovery-codes", UriKind.Relative), TestContext.Current.CancellationToken);

        // Not a 500: the helper deletes the cookie before it tries to unprotect it, so a
        // value that cannot be unprotected clears itself instead of wedging the page.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        SetCookieHeaders(response).Should().Contain(header => header.StartsWith(RecoveryCookieName, StringComparison.Ordinal));

        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        ExtractRecoveryCodes(html).Should().BeEmpty();
        html.Should().Contain("data-testid=\"recovery-empty\"");
    }

    [Fact]
    public async Task A_wrong_code_is_rejected_and_the_user_must_still_enrol()
    {
        (ApplicationUser user, CookieContainer cookies) = await SignedInUserAsync("wrong@example.test");

        using HttpClient client = host.CreateClient(cookies);
        string key = await ReadAuthenticatorKeyAsync(client);

        // A syntactically valid six-digit code that is not the current one. Derived from
        // the real code so it can never collide with it, including across a window roll.
        string wrong = ((int.Parse(CurrentCode(key), CultureInfo.InvariantCulture) + 500000) % 1000000)
            .ToString("D6", CultureInfo.InvariantCulture);

        using (HttpResponseMessage posted = await PostCodeAsync(client, wrong))
        {
            posted.StatusCode.Should().Be(HttpStatusCode.Found);
            posted.Headers.Location?.OriginalString.Should().Be("/account/enrol-totp?error=invalid");
            SetCookieHeaders(posted).Should()
                .NotContain(header => header.StartsWith(RecoveryCookieName, StringComparison.Ordinal));
        }

        await using IdentityDbContext context = host.CreateIdentityContext();
        ApplicationUser stored = await context.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken);

        // MustEnrolTotp is what Task 12's gate reads. If an unverified code could clear
        // it, the gate would be decorative.
        stored.MustEnrolTotp.Should().BeTrue();
        stored.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task The_enrolment_page_is_closed_to_an_unauthenticated_request()
    {
        using HttpClient client = host.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/account/enrol-totp", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Found);

        // Absolute, unlike the relative redirects the account endpoints return: this one
        // comes from the cookie handler's challenge on a Blazor component endpoint, not
        // from a Results.Redirect of ours.
        response.Headers.Location?.OriginalString.Should().Contain("/account/login?ReturnUrl=");

        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.Should().NotContain("data-testid=\"totp-key\"", "no authenticator key may be minted for an anonymous caller");
    }

    [Fact]
    public async Task The_pages_that_render_a_secret_forbid_caching()
    {
        // Both of these put credential material in a response body: the TOTP shared secret
        // on one, ten recovery codes on the other. Shown-once is enforced by deleting a
        // cookie, which stops the server rendering them again and says nothing about the
        // copy the browser kept -- a disk cache or a back-navigation puts them back on
        // screen for whoever is at the machine next.
        //
        // NOTHING IN THIS APPLICATION SETS THIS HEADER. Measured, not assumed: with no
        // `no-store` anywhere under src/, every static-SSR page in this host already answers
        // "no-store, no-cache" -- /account/enrol-totp, /account/recovery-codes,
        // /account/change-password, /account/login and /setup alike. The source is
        // DefaultAntiforgery.SetDoNotCacheHeaders, which Blazor's endpoint renderer triggers
        // by storing an antiforgery token on every component render, plus the health-check
        // middleware doing the same for /health. Adding an explicit header on these two
        // pages would be a line no mutation could redden.
        //
        // So this test exists to pin a property the framework currently supplies rather than
        // to prove application code supplies it. If a future ASP.NET Core stops storing that
        // token eagerly, or a page opts out, these two pages become cacheable with a secret
        // in the body and this is the only thing that would say so.
        (_, CookieContainer cookies) = await SignedInUserAsync("no-store@example.test");

        using HttpClient client = host.CreateClient(cookies);

        CacheControlOf(await client.GetAsync(
            new Uri("/account/enrol-totp", UriKind.Relative), TestContext.Current.CancellationToken))
            .Should().Contain("no-store", "the enrolment page renders the shared secret");

        string key = await ReadAuthenticatorKeyAsync(client);
        using (HttpResponseMessage posted = await PostCodeAsync(client, CurrentCode(key)))
        {
            posted.StatusCode.Should().Be(HttpStatusCode.Found);
        }

        CacheControlOf(await client.GetAsync(
            new Uri("/account/recovery-codes", UriKind.Relative), TestContext.Current.CancellationToken))
            .Should().Contain("no-store", "the recovery-code page renders ten live credentials");
    }

    [Fact]
    public async Task An_enrolled_user_is_refused_the_enrolment_page()
    {
        // The page displays the account's live TOTP shared secret. [Authorize] alone means
        // any authenticated session can re-read it -- including one held by whoever stole
        // the cookie -- so a password change no longer undoes the compromise. Task 12's
        // gate confines flagged users TO this page; nothing kept enrolled users OFF it.
        (ApplicationUser user, CookieContainer cookies) = await EnrolledUserAsync("reread@example.test");

        using HttpClient client = host.CreateClient(cookies);
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/account/enrol-totp", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        LocationPath(response).Should().Be("/", "a user who has already enrolled has no business here");

        // The status alone would be satisfied by a redirect issued after the secret had
        // been rendered into the body -- static SSR writes the markup even on the request
        // that redirects, so the guard has to sit before the key is read, not after.
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.Should().NotContain(
            Formatted(await host.ReadAuthenticatorKeyAsync(user.Id)),
            "the shared secret must not reach the response even on the way out");

        await using IdentityDbContext context = host.CreateIdentityContext();
        ApplicationUser stored = await context.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken);

        stored.TwoFactorEnabled.Should().BeTrue("the refusal must not have disturbed the enrolment");
    }

    [Fact]
    public async Task An_enrolled_user_is_refused_the_verification_endpoint()
    {
        // The endpoint's tail is GenerateNewTwoFactorRecoveryCodesAsync, which REPLACES the
        // stored set. Reachable by any session, it turns a stolen cookie into a silent
        // destruction of the ten codes the owner wrote down -- a denial of the second
        // factor, from a caller who never proved they hold one.
        (ApplicationUser user, string key, CookieContainer cookies) =
            await EnrolledUserWithKeyAsync("regenerate@example.test");

        string[] issued = await host.GenerateRecoveryCodesAsync(user.Id, ExpectedRecoveryCodes);
        issued.Should().HaveCount(ExpectedRecoveryCodes);

        using HttpClient client = host.CreateClient(cookies);

        // A genuinely valid code, computed from the account's own key. The refusal has to be
        // the guard rather than a rejected code, or this proves nothing.
        using HttpResponseMessage response = await AntiforgeryHelper.PostAsync(
            client,
            AntiforgeryHelper.SignedInTokenPage,
            "/account/enrol-totp/verify",
            ("code", CurrentCode(key)));

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        LocationPath(response).Should().Be("/");
        SetCookieHeaders(response).Should()
            .NotContain(header => header.StartsWith(RecoveryCookieName, StringComparison.Ordinal));

        (await host.CountRecoveryCodesAsync(user.Id)).Should().Be(
            ExpectedRecoveryCodes, "no code may have been spent or replaced");

        // The decisive check. A count alone would be satisfied by a fresh set of the same
        // size, which is exactly the outcome this guard exists to prevent, so redeem one of
        // the codes the user actually holds.
        using HttpClient redeeming = host.CreateClient(new CookieContainer());
        using (HttpResponseMessage passwordStep = await SignInHelper.PostPasswordAsync(
            redeeming, user.UserName!, EnrolledPassword))
        {
            passwordStep.Headers.Location?.OriginalString.Should().Be("/account/login-2fa");
        }

        using HttpResponseMessage redeemed = await SignInHelper.PostCodeAsync(
            redeeming, "/account/login-recovery/submit", issued[0]);

        redeemed.Headers.Location?.OriginalString.Should().Be(
            "/", "the recovery codes the user wrote down must survive somebody else's post");
    }

    [Fact]
    public async Task Returning_to_the_enrolment_page_reuses_the_authenticator_key()
    {
        // Spec section 8: a user who leaves before acknowledging the recovery codes comes
        // back here. Resetting the key would silently break the entry already added to
        // their authenticator app, and the next code it shows would be rejected with no
        // explanation.
        (_, CookieContainer cookies) = await SignedInUserAsync("idempotent@example.test");

        using HttpClient client = host.CreateClient(cookies);

        string first = await ReadAuthenticatorKeyAsync(client);
        string second = await ReadAuthenticatorKeyAsync(client);

        second.Should().Be(first);
    }

    [GeneratedRegex("""data-testid="totp-key"[^>]*>([^<]*)<""")]
    private static partial Regex TotpKeyCell();

    /// <summary>
    /// The text of each paragraph inside the display panel. Reading the markup rather
    /// than matching a code alphabet: Identity's recovery-code alphabet is an internal
    /// detail, and a guess at it silently under-counts — an earlier version of this test
    /// found six of ten because the guessed alphabet omitted a digit.
    /// </summary>
    [GeneratedRegex("<p[^>]*>([^<]+)</p>")]
    private static partial Regex Paragraph();

    /// <summary>
    /// The current RFC 6238 code for a base32 key. Identity's authenticator provider is
    /// standard TOTP — HMAC-SHA1, six digits, a thirty-second step — so a standard
    /// library agrees with it. That agreement is an assumption until a test submits a
    /// computed code and the endpoint accepts it, which is what these tests do.
    /// </summary>
    private static string CurrentCode(string key) =>
        new Totp(Base32Encoding.ToBytes(key)).ComputeTotp();

    /// <summary>
    /// The key as the page would print it — four-character groups. Searching the markup for
    /// the raw base32 would miss it, because the page never writes it that way.
    /// </summary>
    private static string Formatted(string key) =>
        string.Join(
            ' ',
            Enumerable.Range(0, (key.Length + 3) / 4)
                .Select(group => key.Substring(group * 4, Math.Min(4, key.Length - (group * 4)))));

    private static string[] ExtractRecoveryCodes(string html)
    {
        const string Marker = "data-testid=\"recovery-codes\"";

        int start = html.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        int end = html.IndexOf("</div>", start, StringComparison.Ordinal);
        string block = end < 0 ? html[start..] : html[start..end];

        return [.. Paragraph().Matches(block).Select(match => match.Groups[1].Value.Trim())];
    }

    /// <summary>
    /// The response's <c>Cache-Control</c> as a string, disposing the response — these
    /// assertions want the header and nothing else from it.
    /// </summary>
    private static string CacheControlOf(HttpResponseMessage response)
    {
        using (response)
        {
            return response.Headers.CacheControl?.ToString() ?? string.Empty;
        }
    }

    private static IEnumerable<string> SetCookieHeaders(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? values : [];

    private static string SetCookieValue(HttpResponseMessage response, string name)
    {
        string header = SetCookieHeaders(response)
            .SingleOrDefault(candidate => candidate.StartsWith(name + "=", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The response set no {name} cookie.");

        string value = header[(name.Length + 1)..];
        int end = value.IndexOf(';', StringComparison.Ordinal);

        return end < 0 ? value : value[..end];
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {path} must render");

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string> ReadAuthenticatorKeyAsync(HttpClient client)
    {
        string html = await GetAsync(client, "/account/enrol-totp");

        Match match = TotpKeyCell().Match(html);
        match.Success.Should().BeTrue("the enrolment page must display a manual entry key");

        return match.Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static async Task<string[]> ReadRecoveryCodesAsync(HttpClient client) =>
        ExtractRecoveryCodes(await GetAsync(client, "/account/recovery-codes"));

    private static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string code) =>
        await SignInHelper.PostCodeAsync(client, "/account/enrol-totp/verify", code);

    /// <summary>
    /// The path a redirect points at. A <c>Results.Redirect</c> from an endpoint emits a
    /// relative location; a <c>NavigationManager.NavigateTo</c> from a static-SSR page emits
    /// an absolute one, and both spellings appear in these tests.
    /// </summary>
    private static string LocationPath(HttpResponseMessage response)
    {
        Uri location = response.Headers.Location
            ?? throw new InvalidOperationException("A redirect with no Location header.");

        return location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
    }

    private async Task<(ApplicationUser User, CookieContainer Cookies)> SignedInUserAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, TestContext.Current.CancellationToken);

        CookieContainer cookies = new();
        cookies.Add(new Uri(host.BaseAddress), await host.CreateAuthenticationCookieAsync(user));

        return (user, cookies);
    }

    /// <summary>A signed-in client for a user who has finished enrolling.</summary>
    private async Task<(ApplicationUser User, CookieContainer Cookies)> EnrolledUserAsync(string email)
    {
        (ApplicationUser user, _, CookieContainer cookies) = await EnrolledUserWithKeyAsync(email);

        return (user, cookies);
    }

    /// <inheritdoc cref="EnrolledUserAsync(string)"/>
    /// <remarks>
    /// The cookie is minted from the user as they are <b>after</b> enrolment, re-read from
    /// the store: <c>SetTwoFactorEnabledAsync</c> rotates the security stamp, and a ticket
    /// carrying the stamp the account held before it would be ended by the validator.
    /// </remarks>
    private async Task<(ApplicationUser User, string Key, CookieContainer Cookies)> EnrolledUserWithKeyAsync(
        string email)
    {
        ApplicationUser created =
            await host.CreateUserAsync(email, EnrolledPassword, TestContext.Current.CancellationToken);
        string key = await host.EnableTwoFactorAsync(created.Id);

        await using IdentityDbContext context = host.CreateIdentityContext();
        ApplicationUser enrolled = await context.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == created.Id, TestContext.Current.CancellationToken);

        CookieContainer cookies = new();
        cookies.Add(new Uri(host.BaseAddress), await host.CreateAuthenticationCookieAsync(enrolled));

        return (enrolled, key, cookies);
    }

    private async Task<string> ReadTokenValueAsync(Guid userId, string name)
    {
        await using NpgsqlConnection connection = new(host.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """SELECT "Value" FROM identity."AspNetUserTokens" WHERE "UserId" = @userId AND "Name" = @name""";
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("name", name);

        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        value.Should().NotBeNull($"the {name} token must have been written");

        return value!.ToString()!;
    }
}
