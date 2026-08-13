using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// Posts to the <c>/account</c> endpoints the way a browser does: fetch a page, take the
/// antiforgery token the form rendered, send it back with the fields.
/// <para>
/// Every one of those endpoints requires a valid token
/// (<c>AccountEndpoints.MapAccountEndpoints</c> puts
/// <c>RequireAntiforgeryTokenAttribute</c> on the group), so a hand-rolled
/// <c>client.PostAsync</c> answers 400 and proves nothing about the handler behind it.
/// </para>
/// <para>
/// The client must keep cookies. Validation compares the request token against a cookie
/// token, so a client with no jar can never satisfy it — use
/// <c>SetupHostFixture.CreateClient(CookieContainer)</c>.
/// </para>
/// </summary>
internal static partial class AntiforgeryHelper
{
    // internal const Fields

    /// <summary>The hidden field Blazor's <c>&lt;AntiforgeryToken /&gt;</c> renders.</summary>
    public const string FieldName = "__RequestVerificationToken";

    /// <summary>
    /// Where a caller with no session takes a token from.
    /// <para>
    /// <c>/account/login-2fa</c> rather than <c>/account/login</c> deliberately: it renders
    /// unconditionally, while the sign-in page redirects to <c>/setup</c> while no user
    /// exists. A token is bound to the caller, not to a form's action, so which page issued
    /// it does not matter — only which caller did.
    /// </para>
    /// </summary>
    public const string AnonymousTokenPage = "/account/login-2fa";

    /// <summary>
    /// Where a signed-in caller takes a token from. <c>/account/change-password</c> is the
    /// one page every authenticated user can open: it needs no permission, and it is on the
    /// enrolment gate's allowlist, so a user who still owes TOTP enrolment reaches it too.
    /// </summary>
    public const string SignedInTokenPage = "/account/change-password";

    // internal static Methods

    /// <summary>The token a rendered page carries, or a failed assertion naming the page.</summary>
    public static async Task<string> TokenFromAsync(HttpClient client, string page)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(page, UriKind.Relative), TestContext.Current.CancellationToken);

        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Match input = TokenInput().Match(html);
        input.Success.Should().BeTrue(
            $"GET {page} answered {(int)response.StatusCode} and must render an antiforgery token for the post that follows");

        Match value = AttributeValue().Match(input.Value);
        value.Success.Should().BeTrue($"the {FieldName} input on {page} must carry a value");

        return value.Groups[1].Value;
    }

    /// <summary>Fetches a token from <paramref name="tokenPage"/>, then posts with it.</summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string tokenPage,
        string path,
        params (string Name, string Value)[] fields) =>
        await PostWithTokenAsync(client, await TokenFromAsync(client, tokenPage), path, fields);

    /// <summary>
    /// Posts with a token the caller already holds. Antiforgery tokens are not single-use,
    /// so a test making several posts in a row can fetch one and reuse it — which is also
    /// what keeps a test about the rate limiter counting only the posts it means to count.
    /// </summary>
    public static async Task<HttpResponseMessage> PostWithTokenAsync(
        HttpClient client,
        string token,
        string path,
        params (string Name, string Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(client);

        (string Name, string Value)[] withToken = [.. fields, (FieldName, token)];

        return await PostWithoutTokenAsync(client, path, withToken);
    }

    /// <summary>
    /// Posts exactly the fields given and nothing else. Named for what it is, because the
    /// only reason to reach for it is to assert that the post is <b>refused</b>.
    /// </summary>
    public static async Task<HttpResponseMessage> PostWithoutTokenAsync(
        HttpClient client,
        string path,
        params (string Name, string Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(client);

        using FormUrlEncodedContent form =
            new([.. fields.Select(field => new KeyValuePair<string, string>(field.Name, field.Value))]);

        return await client.PostAsync(
            new Uri(path, UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    // private static Methods

    /// <summary>
    /// The whole hidden input, matched by name. Extracting the value in a second step keeps
    /// this independent of the order the renderer emits the attributes in.
    /// </summary>
    [GeneratedRegex($"""<input[^>]*name="{FieldName}"[^>]*>""")]
    private static partial Regex TokenInput();

    [GeneratedRegex("value=\"([^\"]+)\"")]
    private static partial Regex AttributeValue();
}
