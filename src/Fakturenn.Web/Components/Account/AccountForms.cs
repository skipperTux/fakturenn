using System.Collections.Frozen;
using Microsoft.AspNetCore.Http.Features;

namespace Fakturenn.Web.Components.Account;

/// <summary>
/// Where a refused <c>/account</c> post sends the browser, and what it may take with it.
/// <para>
/// A form's action is never its own page route in this application — see the note at the top
/// of <see cref="AccountEndpoints"/> — so an endpoint that refuses a submission cannot simply
/// re-render; it has to name the page that drew the form. That mapping lives here rather than
/// in a string literal per handler, because two callers need it: every handler in
/// <see cref="AccountEndpoints"/>, and <see cref="AntiforgeryFailureMiddleware"/>, which
/// refuses the request before any handler runs and therefore has nothing but the path to go on.
/// </para>
/// <para>
/// The fields carried back are an <b>allowlist</b>, never a denylist of secrets. A denylist is
/// one forgotten entry away from putting a password in a redirect URL, where it lands in the
/// browser's history and in every reverse proxy's access log; a field added to a form is not
/// echoed back until somebody adds it here on purpose. That is why <c>password</c>,
/// <c>currentPassword</c>, <c>newPassword</c> and <c>code</c> need no mention below — being
/// absent is what excludes them.
/// </para>
/// </summary>
internal static class AccountForms
{
    // private const Fields

    private const string SignInPage = "/account/login";

    private const string AdminUsersPage = "/admin/users";

    // internal const Fields

    /// <summary>
    /// The <c>?error=</c> value <see cref="Expired"/> uses, and the one sentinel every form
    /// page understands. <c>FormError</c> turns it into the localized sentence.
    /// <para>
    /// A sentinel rather than the message itself, and not only for tidiness. Three of these
    /// pages render the <c>?error=</c> value verbatim, because it carries Identity's own
    /// localized validation descriptions — and one of the pages an antiforgery failure can
    /// land on is the <b>sign-in page</b>. A redirect that put arbitrary text on a sign-in
    /// page would be a phishing lever handed to whoever composed the URL, so the antiforgery
    /// path passes a name and the application supplies the words.
    /// </para>
    /// </summary>
    internal const string ExpiredError = "expired";

    // private static readonly Fields

    /// <summary>
    /// The page that renders the form each endpoint answers.
    /// <para>
    /// <c>AccountFormsTests.Every_account_post_endpoint_names_the_page_that_renders_its_form</c>
    /// cross-checks this against the real route table, because a missing entry has no compiler
    /// error behind it: the handler would throw at the moment a user's submission was refused,
    /// which is the least convenient time to discover it.
    /// </para>
    /// </summary>
    private static readonly FrozenDictionary<string, (string Page, string Form)> _formOfEndpoint =
        new Dictionary<string, (string Page, string Form)>(StringComparer.Ordinal)
        {
            ["/account/setup"] = ("/setup", "setup"),
            ["/account/login/submit"] = (SignInPage, "sign-in"),
            ["/account/login-2fa/submit"] = ("/account/login-2fa", "authenticator"),
            ["/account/login-recovery/submit"] = ("/account/login-recovery", "recovery-code"),
            ["/account/enrol-totp/verify"] = ("/account/enrol-totp", "enrolment"),
            ["/account/change-password/submit"] = ("/account/change-password", "change-password"),

            // Nothing to re-render, but a refused sign-out still has to answer something: the
            // sign-in page is where a caller whose session is in doubt belongs.
            ["/account/logout"] = (SignInPage, "sign-out"),

            ["/account/admin/create-user"] = (AdminUsersPage, "create-user"),
            ["/account/admin/reset-password"] = (AdminUsersPage, "reset-password"),
            ["/account/admin/clear-mfa"] = (AdminUsersPage, "clear-mfa"),
            ["/account/admin/set-lockout"] = (AdminUsersPage, "set-lockout"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The only form fields a refused submission hands back. Both are things the user typed
    /// and can already see; neither is a credential.
    /// </summary>
    private static readonly string[] _preservedFields = ["email", "displayName"];

    // internal static Properties

    /// <summary>Every endpoint named above, for the guard that checks the table against the routes.</summary>
    internal static IReadOnlyCollection<string> MappedEndpoints => _formOfEndpoint.Keys;

    // internal static Methods

    /// <summary>
    /// Sends the browser back to the form with a message and the non-secret fields it carried,
    /// so a rejected submission costs the user a correction rather than a retype.
    /// <para>
    /// For a handler only. Reading the form is safe here precisely because the handler ran,
    /// which means the antiforgery token was accepted — see <see cref="Expired"/> for the case
    /// where it was not.
    /// </para>
    /// </summary>
    internal static IResult Rejected(HttpContext http, string message)
    {
        ArgumentNullException.ThrowIfNull(http);

        string endpoint = EndpointOf(http);
        if (!_formOfEndpoint.TryGetValue(endpoint, out (string Page, string Form) expected))
        {
            throw new InvalidOperationException(
                $"No form page is mapped for {endpoint}. Add it to AccountForms.");
        }

        string location = $"{expected.Page}?error={Uri.EscapeDataString(message)}";

        // The parsed form off the feature, never Request.Form: the property getter parses the
        // body synchronously when nothing has read it yet, and Kestrel forbids synchronous
        // body reads. A handler always arrives after model binding has read it, so what is
        // wanted here is the copy that already exists.
        IFormCollection? form = http.Features.Get<IFormFeature>()?.Form;

        string[] preserved = form is null
            ? []
            : [.. _preservedFields
                .Select(field => (Field: field, Value: form[field].ToString()))
                .Where(entry => !string.IsNullOrEmpty(entry.Value))
                .Select(entry => $"{entry.Field}={Uri.EscapeDataString(entry.Value)}")];

        if (preserved.Length == 0)
        {
            return Results.Redirect(location);
        }

        // Which form the values belong to, and only when there are values to place:
        // /admin/users hosts both "create a user" and "reset a password", and each has an
        // "email" field, so without this the address typed into one comes back filled into
        // the other — and "reset this person's password" is not a form to put an address
        // into that nobody asked for. Single-form pages ignore it.
        return Results.Redirect($"{location}&form={expected.Form}&{string.Join('&', preserved)}");
    }

    /// <summary>
    /// Where a post whose antiforgery token was refused goes: the page that drew the form, with
    /// a message and <b>nothing else</b>. <see langword="null"/> when the path is not one of
    /// this application's form endpoints, which is how
    /// <see cref="AntiforgeryFailureMiddleware"/> tells "a form of ours was refused" from
    /// "something else was" without keeping a second list of paths in step with this one.
    /// <para>
    /// It deliberately carries no fields back, and that is not an oversight in the
    /// preserve-what-was-typed behaviour above. A post that failed antiforgery validation is
    /// exactly the post that may have been composed by somebody else's page, so its fields are
    /// attacker-controlled input, not the user's own typing. ASP.NET Core enforces the same
    /// rule from its side: <c>FormFeature.Form</c> throws
    /// <c>InvalidOperationException("This form is being accessed with an invalid anti-forgery
    /// token")</c> once <see cref="Microsoft.AspNetCore.Antiforgery.IAntiforgeryValidationFeature"/>
    /// reports a failure — measured, as a 500 from an earlier version of this file that tried
    /// to read it.
    /// </para>
    /// </summary>
    internal static string? Expired(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return _formOfEndpoint.TryGetValue(EndpointOf(http), out (string Page, string Form) form)
            ? $"{form.Page}?error={ExpiredError}"
            : null;
    }

    // private static Methods

    private static string EndpointOf(HttpContext http) => http.Request.Path.Value ?? string.Empty;
}
