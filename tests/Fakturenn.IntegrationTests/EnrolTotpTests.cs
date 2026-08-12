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

    private static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string code)
    {
        using FormUrlEncodedContent form = new([new KeyValuePair<string, string>("code", code)]);

        return await client.PostAsync(
            new Uri("/account/enrol-totp/verify", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    private async Task<(ApplicationUser User, CookieContainer Cookies)> SignedInUserAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, TestContext.Current.CancellationToken);

        CookieContainer cookies = new();
        cookies.Add(new Uri(host.BaseAddress), await host.CreateAuthenticationCookieAsync(user));

        return (user, cookies);
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
