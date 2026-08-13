using System.Net;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;
using Fakturenn.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// What happens to a form that is submitted later than the session behind it expected.
/// <para>
/// This class exists because a suite of 275 green tests missed a defect a human found in
/// about ninety seconds of using the application: every one of them submits its form within
/// milliseconds of rendering it, and the failure needs the submission to arrive after
/// <c>SecurityStampValidatorOptions.ValidationInterval</c> — one minute — has elapsed. Typing
/// a six-digit code off a phone takes longer than that.
/// </para>
/// </summary>
[Collection(RealHost.Name)]
public sealed class StaleFormTests(SetupHostFixture host)
{
    /// <summary>Satisfies the configured policy: twelve characters, upper, lower, digit.</summary>
    private const string Password = "Korrekt-Pferd-42";

    /// <summary>
    /// Comfortably past the one-minute <c>ValidationInterval</c>, so the next request
    /// revalidates the security stamp rather than trusting the ticket.
    /// </summary>
    private static readonly TimeSpan _pastTheValidationInterval = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task A_code_typed_after_the_validation_interval_still_enrols()
    {
        // THE regression test. Reproduced live, twice: sign in 17:47:37, enrolment page
        // rendered 17:47:37, code posted 17:48:53 and logged as a 500, and the next page load
        // was already signed out.
        //
        // The mechanism: rendering the enrolment page mints the authenticator key, which
        // rotates the security stamp, while the cookie in the browser still carries the stamp
        // it was issued under. Inside the first minute nothing revalidates, so the session
        // looks healthy; on the first request after it the validator finds a mismatch,
        // rejects the principal and signs the user out -- and the antiforgery token the page
        // rendered had been bound to a signed-in caller who is no longer there.
        ApplicationUser user = await host.CreateUserAsync(
            "stale-enrolment@example.test", Password, TestContext.Current.CancellationToken);

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies);

        // Through the real endpoint, because the cookie has to be the one the application
        // issues rather than one the fixture mints.
        using (HttpResponseMessage passwordStep =
            await SignInHelper.PostPasswordAsync(client, user.UserName!, Password))
        {
            passwordStep.Headers.Location?.OriginalString.Should().Be(
                "/", "the account owes an enrolment, so the password alone signs it in");
        }

        // The GET that mints the key, and therefore the GET that rotates the stamp.
        string token = await AntiforgeryHelper.TokenFromAsync(client, "/account/enrol-totp");
        string key = await host.ReadAuthenticatorKeyAsync(user.Id);

        host.AgeAuthenticationCookie(cookies, _pastTheValidationInterval);

        using HttpResponseMessage posted = await AntiforgeryHelper.PostWithTokenAsync(
            client,
            token,
            "/account/enrol-totp/verify",
            ("code", SignInHelper.CurrentCode(key)));

        posted.StatusCode.Should().Be(HttpStatusCode.Found);
        posted.Headers.Location?.OriginalString.Should().Be(
            "/account/recovery-codes",
            "the enrolment page must re-issue the cookie under the stamp it just rotated; "
            + "without that the session is already gone by the time the code is typed");

        await using IdentityDbContext context = host.CreateIdentityContext();
        ApplicationUser stored = await context.Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken);

        stored.TwoFactorEnabled.Should().BeTrue();
        stored.MustEnrolTotp.Should().BeFalse("a verified code is what clears the enrolment gate");
    }

    [Fact]
    public async Task A_refused_antiforgery_token_answers_with_the_form_and_a_sentence()
    {
        // The other half, and the one that must hold whatever else is fixed: a token can
        // still go stale -- a tab left open past the cookie's life, a sign-out in another
        // window -- and the answer must be something a person can act on.
        //
        // Before this, RequestDelegateFactory threw a BadHttpRequestException that nothing in
        // the pipeline handled. Measured on a real Development instance: the wire answer was
        // 400 carrying the developer exception page's body, and the request log recorded
        // "responded 500" -- Serilog's request logging catches and rethrows before that page
        // sees the exception, so the operator and the user were told different things about
        // the same request. Under Production it is a bare 400 with no body.
        ApplicationUser user = await host.CreateUserAsync(
            "refused-token@example.test", Password, TestContext.Current.CancellationToken);

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies);

        using (HttpResponseMessage passwordStep =
            await SignInHelper.PostPasswordAsync(client, user.UserName!, Password))
        {
            passwordStep.StatusCode.Should().Be(HttpStatusCode.Found);
        }

        string stampBefore =
            await host.ReadSecurityStampAsync(user.Id, TestContext.Current.CancellationToken);

        using (HttpResponseMessage refused = await AntiforgeryHelper.PostWithoutTokenAsync(
            client,
            "/account/change-password/submit",
            ("currentPassword", Password),
            ("newPassword", "Anderes-Pferd-77")))
        {
            refused.StatusCode.Should().Be(
                HttpStatusCode.Found,
                "a stale token must answer with somewhere to go, not with an error status and "
                + "an exception page");
            refused.Headers.Location?.OriginalString.Should().Be("/account/change-password?error=expired");
        }

        (await host.ReadSecurityStampAsync(user.Id, TestContext.Current.CancellationToken))
            .Should().Be(stampBefore, "the post must still be refused -- no handler may have run");

        // And the page the browser is sent to says what happened, in words rather than in a
        // sentinel. FormError is what maps the one to the other.
        using HttpResponseMessage page = await client.GetAsync(
            new Uri("/account/change-password?error=expired", UriKind.Relative),
            TestContext.Current.CancellationToken);

        string html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("data-testid=\"change-password-error\"");
        html.Should().Contain(
            "had been open too long",
            "the sentence comes from Account_Error_FormExpired, which SharedResourceTests "
            + "keeps present and translated in both languages");
        html.Should().NotContain(
            ">expired<", "the sentinel is a name for the application, never text for a user");
    }
}
