using System.Net;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Domain;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// The <c>account</c> rate limiter's partition key, driven over HTTP through the real
/// host — the limiter runs as pipeline middleware, so nothing short of a real request
/// exercises the key it actually computes.
/// </summary>
[Collection(RealHost.Name)]
public sealed class AccountRateLimitTests(SetupHostFixture host)
{
    private const string Password = "Korrekt-Pferd-42";

    /// <summary>
    /// Above the limiter's ten-per-minute permit when two users share one budget, below it
    /// when each has their own. Six and six is the smallest pair that separates the two.
    /// </summary>
    private const int PostsPerUser = 6;

    /// <summary>
    /// One address for both users, which is the deployment this test exists for: the
    /// documented safe default configures no forwarded-header trust, so every client
    /// behind a reverse proxy or a NAT arrives as the proxy's address.
    /// </summary>
    private static IPAddress SharedAddress => IPAddress.Parse("127.0.0.30");

    [Fact]
    public async Task Two_users_behind_one_address_do_not_share_a_budget()
    {
        // The exact failure this key was changed to fix. Under a key of address alone the
        // two users' twelve posts land in one ten-permit partition and the last two answer
        // 429 -- a five-person office behind one address locking itself out of its own
        // second factor, with no configuration mistake to point at.
        List<HttpStatusCode> first = await FailingChangePasswordAttemptsAsync("shared-a@example.test");
        List<HttpStatusCode> second = await FailingChangePasswordAttemptsAsync("shared-b@example.test");

        first.Should().AllBeEquivalentTo(HttpStatusCode.Found);
        second.Should().AllBeEquivalentTo(
            HttpStatusCode.Found,
            "the second user's budget must be their own, but the responses were: {0}",
            string.Join(", ", second));
    }

    [Fact]
    public async Task An_unreadable_two_factor_cookie_falls_through_instead_of_failing_the_request()
    {
        // The identity branch unprotects a ticket. A value it cannot read must fall through
        // to the anonymous key rather than throw inside the limiter, which would turn a
        // junk cookie into a 500 on an unauthenticated endpoint.
        CookieContainer cookies = new();
        cookies.Add(
            new Uri(host.BaseAddress),
            new Cookie("Identity.TwoFactorUserId", "not-a-protected-ticket", "/"));

        using HttpClient client = host.CreateClient(cookies, SharedAddress);

        using HttpResponseMessage response =
            await SignInHelper.PostCodeAsync(client, "/account/login-2fa/submit", "000000");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be("/account/login-2fa?error=invalid");
    }

    /// <summary>
    /// Signs a user in, then posts a change of password that cannot succeed. A rejected
    /// change is the ideal probe: it is authenticated, so it exercises the signed-in half
    /// of the key; it is idempotent; and unlike a failed sign-in it neither counts towards
    /// lockout nor rotates the security stamp, so nothing but the limiter can change the
    /// answer.
    /// </summary>
    private async Task<List<HttpStatusCode>> FailingChangePasswordAttemptsAsync(string email)
    {
        ApplicationUser user = await host.CreateUserAsync(email, Password, TestContext.Current.CancellationToken);
        string key = await host.EnableTwoFactorAsync(user.Id);

        CookieContainer cookies = new();
        using HttpClient client = host.CreateClient(cookies, SharedAddress);

        await SignInHelper.SignInAsync(client, email, Password, key);

        // One token for all six posts, deliberately. Antiforgery tokens are not single-use,
        // and re-fetching one per attempt would leave the count of requests this method
        // makes to the "account" group depending on how the helper is written -- which is
        // precisely the number this test is measuring.
        string token = await AntiforgeryHelper.TokenFromAsync(client, AntiforgeryHelper.SignedInTokenPage);

        List<HttpStatusCode> statuses = [];
        for (int attempt = 0; attempt < PostsPerUser; attempt++)
        {
            using HttpResponseMessage response = await AntiforgeryHelper.PostWithTokenAsync(
                client,
                token,
                "/account/change-password/submit",
                ("currentPassword", "Falsch-Pferd-99"),
                ("newPassword", "Anderes-Pferd-77"));

            statuses.Add(response.StatusCode);
        }

        return statuses;
    }
}
