using System.Net;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// The enrolment gate, driven over HTTP through the real host.
/// <para>
/// The gate is what turns <c>MustEnrolTotp</c> and <c>MustChangePassword</c> from a
/// redirect on one response into a condition on every request. A test that only checked
/// "sign-in redirects me" would pass while a user typed a different URL and reached the
/// application anyway, which is the defect this task exists to close.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed class EnrolmentGateTests(SetupHostFixture host)
{
    /// <summary>Satisfies the configured policy: twelve characters, upper, lower, digit.</summary>
    private const string Password = "Korrekt-Pferd-42";

    /// <summary>A page with no <c>[Authorize]</c> — so anything blocking it is the gate.</summary>
    private const string ApplicationPage = "/";

    [Fact]
    public async Task A_user_who_has_not_enrolled_totp_is_confined_to_enrolment()
    {
        using HttpClient client = await NotEnrolledClientAsync("gate-enrol@example.test");

        using HttpResponseMessage response = await GetAsync(client, ApplicationPage);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be("/account/enrol-totp");
    }

    [Theory]
    [InlineData("/account/enrol-totp")]
    [InlineData("/account/recovery-codes")]
    public async Task The_pages_an_enrolling_user_still_needs_stay_reachable(string path)
    {
        using HttpClient client = await NotEnrolledClientAsync($"gate-reach-{Slug(path)}@example.test");

        using HttpResponseMessage response = await GetAsync(client, path);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, "a user the gate confines to a page must be able to open that page");
    }

    [Fact]
    public async Task The_enrolment_form_still_posts_while_the_flag_is_set()
    {
        using HttpClient client = await NotEnrolledClientAsync("gate-enrol-post@example.test");

        using FormUrlEncodedContent form = new([new KeyValuePair<string, string>("code", "000000")]);
        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/account/enrol-totp/verify", UriKind.Relative), form, TestContext.Current.CancellationToken);

        // The query string is the whole assertion. The gate's own redirect is to
        // "/account/enrol-totp" with nothing after it, so only "?error=invalid" proves the
        // post reached the verification handler rather than being bounced by the gate.
        response.Headers.Location?.OriginalString.Should().Be("/account/enrol-totp?error=invalid");
    }

    [Fact]
    public async Task Signing_out_still_works_while_the_flag_is_set()
    {
        using HttpClient client = await NotEnrolledClientAsync("gate-logout@example.test");

        using FormUrlEncodedContent form = new([]);
        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/account/logout", UriKind.Relative), form, TestContext.Current.CancellationToken);

        response.Headers.Location?.OriginalString.Should().Be(
            "/account/login", "a user who cannot finish enrolling must still be able to leave");
    }

    [Fact]
    public async Task A_user_who_must_change_the_password_is_confined_to_the_change_form()
    {
        using HttpClient client = await MustChangePasswordClientAsync("gate-change@example.test");

        using HttpResponseMessage response = await GetAsync(client, ApplicationPage);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be("/account/change-password");
    }

    [Fact]
    public async Task The_forced_password_change_settles_on_a_page_rather_than_looping()
    {
        // The destination the gate redirects to has to be a destination the gate allows.
        // Drop "/account/change-password" from the allowlist and this is what happens: the
        // gate answers the redirect it just issued with the same redirect, forever. A test
        // that only asserted "blocked pages redirect" stays green while the application is
        // unusable, so this one follows the chain instead of inspecting one hop.
        using HttpClient client = await MustChangePasswordClientAsync("gate-change-loop@example.test");

        List<string> visited = [];
        string path = ApplicationPage;

        for (int hop = 0; hop < 5; hop++)
        {
            visited.Add(path);

            using HttpResponseMessage response = await GetAsync(client, path);
            if (response.StatusCode != HttpStatusCode.Found)
            {
                response.StatusCode.Should().Be(
                    HttpStatusCode.OK, $"the redirect chain {string.Join(" -> ", visited)} must end on a page");

                visited.Should().OnlyHaveUniqueItems("a repeated path is a redirect loop");
                return;
            }

            path = response.Headers.Location?.OriginalString
                ?? throw new InvalidOperationException("A 302 with no Location header.");
        }

        Assert.Fail($"The redirect chain did not settle: {string.Join(" -> ", visited)}");
    }

    [Fact]
    public async Task The_change_password_form_still_posts_while_the_flag_is_set()
    {
        using HttpClient client = await MustChangePasswordClientAsync("gate-change-post@example.test");

        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("currentPassword", "Falsch-Pferd-99"),
            new KeyValuePair<string, string>("newPassword", "Anderes-Pferd-77"),
        ]);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/account/change-password/submit", UriKind.Relative), form, TestContext.Current.CancellationToken);

        // Same reasoning as the enrolment post: the gate redirects to the bare page, so the
        // "?error=" the handler adds is what distinguishes "the handler ran and refused" from
        // "the gate bounced it".
        response.Headers.Location?.OriginalString.Should().StartWith("/account/change-password?error=");
    }

    [Fact]
    public async Task A_user_with_neither_flag_reaches_the_application()
    {
        // The positive control. Without it, a gate that redirected everybody would satisfy
        // every assertion above.
        using HttpClient client = await EnrolledClientAsync("gate-clear@example.test");

        using HttpResponseMessage response = await GetAsync(client, ApplicationPage);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_anonymous_client_still_reaches_the_sign_in_page()
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await GetAsync(client, "/account/login");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, "a gate that redirected anonymous callers would close sign-in itself");
    }

    [Fact]
    public async Task An_anonymous_client_can_still_post_credentials()
    {
        // /account/login/submit is NOT on the allowlist, deliberately — an authenticated
        // user has no business there. Anonymous callers reach it because the gate declines
        // to act on a request with no user behind it, and this pins that.
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response =
            await SignInHelper.PostPasswordAsync(client, "nobody@example.test", Password);

        response.Headers.Location?.OriginalString.Should().Be("/account/login?error=invalid");
    }

    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task The_probes_are_never_gated(string path)
    {
        // A probe that depended on some user's enrolment state would stall a rolling
        // deployment for a reason no operator could see from the outside.
        using HttpClient client = await NotEnrolledClientAsync($"gate-probe-{Slug(path)}@example.test");

        using HttpResponseMessage response = await GetAsync(client, path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // There is deliberately NO test here for static assets. This host serves none: it is
    // built from the test project's content root, which has no wwwroot and, running as
    // Production, never loads the static-web-assets manifest either. Every asset request
    // therefore falls through to the gate and is redirected, which is the opposite of what
    // a deployment does. The real behaviour was measured against a `dotnet publish` output
    // instead, and is recorded in IMPLEMENTATION-NOTES.md — asserting it here would pin the
    // test host's artefact rather than the application's behaviour.

    /// <summary>A distinct, readable user name per theory case.</summary>
    private static string Slug(string path) =>
        new([.. path.Where(char.IsLetterOrDigit)]);

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    /// <summary>
    /// A signed-in client for a user who has never enrolled. The password step alone issues
    /// the application cookie here, because <c>TwoFactorEnabled</c> is false until enrolment
    /// completes — that is exactly the state the gate exists to confine.
    /// </summary>
    private async Task<HttpClient> NotEnrolledClientAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, Password, TestContext.Current.CancellationToken);

        HttpClient client = host.CreateClient(new CookieContainer());

        using (HttpResponseMessage response = await SignInHelper.PostPasswordAsync(client, user.UserName!, Password))
        {
            response.Headers.Location?.OriginalString.Should().Be(
                "/", "the password step must sign in a user who has no second factor yet");
        }

        return client;
    }

    private async Task<HttpClient> EnrolledClientAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, Password, TestContext.Current.CancellationToken);
        string key = await host.EnableTwoFactorAsync(user.Id);

        HttpClient client = host.CreateClient(new CookieContainer());
        await SignInHelper.SignInAsync(client, user.UserName!, Password, key);

        return client;
    }

    private async Task<HttpClient> MustChangePasswordClientAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, Password, TestContext.Current.CancellationToken);
        string key = await host.EnableTwoFactorAsync(user.Id);

        HttpClient client = host.CreateClient(new CookieContainer());
        await SignInHelper.SignInAsync(client, user.UserName!, Password, key);

        // Set after the sign-in, by column update, so the security stamp is untouched and
        // the cookie the client just received stays valid. The gate reads the flag on every
        // request, so it takes effect on the next one.
        await host.SetMustChangePasswordAsync(user.Id, value: true, TestContext.Current.CancellationToken);

        return client;
    }
}
