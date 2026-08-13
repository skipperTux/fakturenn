using System.Globalization;
using AwesomeAssertions;
using OtpNet;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// Driving the real sign-in endpoints over HTTP, shared by the classes that need a
/// signed-in client rather than a signed-in client's behaviour.
/// <para>
/// Every code these helpers submit is computed with <c>Otp.NET</c> from the account's own
/// key, so the real <c>UserManager.VerifyTwoFactorTokenAsync</c> runs against real
/// RFC 6238. Nothing here stubs the verifier.
/// </para>
/// </summary>
internal static class SignInHelper
{
    // internal static Methods

    /// <summary>The current RFC 6238 code for a base32 key.</summary>
    public static string CurrentCode(string key) =>
        new Totp(Base32Encoding.ToBytes(key)).ComputeTotp();

    /// <summary>
    /// A syntactically valid six-digit code that is not the current one. Derived from the
    /// real code so it cannot collide with it, including across a window roll.
    /// </summary>
    public static string WrongCode(string key) =>
        ((int.Parse(CurrentCode(key), CultureInfo.InvariantCulture) + 500000) % 1000000)
            .ToString("D6", CultureInfo.InvariantCulture);

    public static async Task<HttpResponseMessage> PostPasswordAsync(
        HttpClient client,
        string email,
        string password) =>
        await AntiforgeryHelper.PostAsync(
            client,
            AntiforgeryHelper.AnonymousTokenPage,
            "/account/login/submit",
            ("email", email),
            ("password", password));

    /// <summary>
    /// Posts a code to one of the code-taking endpoints.
    /// <para>
    /// The token page depends on the caller, not on the path: the two sign-in challenges
    /// are posted by a caller who has no session yet — the two-factor cookie is not one —
    /// while <c>/account/enrol-totp/verify</c> is posted by a signed-in user, and a token
    /// issued to one of those is not valid for the other.
    /// </para>
    /// </summary>
    public static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string path, string code) =>
        await AntiforgeryHelper.PostAsync(
            client,
            path.StartsWith("/account/enrol-totp", StringComparison.Ordinal)
                ? "/account/enrol-totp"
                : AntiforgeryHelper.AnonymousTokenPage,
            path,
            ("code", code));

    /// <summary>Both factors, asserting at each step so a caller's later failure is its own.</summary>
    public static async Task SignInAsync(HttpClient client, string email, string password, string key)
    {
        using (HttpResponseMessage passwordStep = await PostPasswordAsync(client, email, password))
        {
            passwordStep.Headers.Location?.OriginalString.Should().Be(
                "/account/login-2fa", "the password step must reach the challenge before a test can go further");
        }

        using HttpResponseMessage secondFactor =
            await PostCodeAsync(client, "/account/login-2fa/submit", CurrentCode(key));

        secondFactor.Headers.Location?.OriginalString.Should().NotBe("/account/login-2fa?error=invalid");
    }
}
