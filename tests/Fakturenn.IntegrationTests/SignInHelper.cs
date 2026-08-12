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

    public static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string path, string code)
    {
        using FormUrlEncodedContent form = new([new KeyValuePair<string, string>("code", code)]);

        return await client.PostAsync(
            new Uri(path, UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

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
