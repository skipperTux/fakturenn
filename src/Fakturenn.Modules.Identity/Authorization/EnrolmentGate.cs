namespace Fakturenn.Modules.Identity.Authorization;

/// <summary>
/// Decides which paths a signed-in user who still owes the application something —
/// TOTP enrolment, a forced password change — may reach. Kept as a pure function so the
/// policy is testable without a request pipeline.
/// </summary>
public static class EnrolmentGate
{
    // private static readonly Fields

    /// <summary>
    /// Prefix rather than exact match, so the page and the form it posts to are covered by
    /// one entry: <c>/account/enrol-totp</c> also allows <c>/account/enrol-totp/verify</c>,
    /// and <c>/account/change-password</c> also allows <c>/account/change-password/submit</c>.
    /// <para>
    /// Both destinations the gate itself redirects to <b>must</b> appear here. Removing
    /// either one turns the redirect into an infinite loop: the gate sends the user to a
    /// page the gate then blocks, and blocks by redirecting to that same page.
    /// </para>
    /// <para>
    /// <c>/_blazor</c> is deliberately <b>absent</b>. Nothing declares a render mode today,
    /// so no circuit is ever negotiated and the omission costs nothing. When a component
    /// eventually carries <c>@rendermode InteractiveServer</c>, a gated user's circuit
    /// request will be redirected and that page will load but do nothing — which looks like
    /// a broken component, not a gate decision. Failing closed is still the right default:
    /// allowlisting the circuit endpoint would let a gated user render any interactive page
    /// server-side and walk straight past this gate. Whoever introduces interactivity has to
    /// decide what a half-enrolled user's circuit may do, and this comment is where that
    /// decision belongs.
    /// </para>
    /// <para>
    /// Static assets are <b>not</b> listed, and the omission is measured rather than
    /// assumed. Against a <c>dotnet publish</c> output, instrumenting the gate to log every
    /// path it sees showed <c>/app.css</c>, <c>/_framework/blazor.web.js</c> and
    /// <c>/_content/MudBlazor/MudBlazor.min.css</c> answering 200 without the gate seeing
    /// any of them: <c>UseStaticFiles</c> runs before <c>UseAuthentication</c> and
    /// short-circuits them. Entries for those prefixes would be dead code.
    /// <c>/css/</c> and <c>/favicon.ico</c> <i>do</i> reach the gate, because nothing serves
    /// them — this application has neither — so allowlisting them would only turn a redirect
    /// into a 404.
    /// </para>
    /// </summary>
    private static readonly string[] _allowedPrefixes =
    [
        "/account/enrol-totp",
        "/account/recovery-codes",
        "/account/change-password",
        "/account/logout",
        "/alive",
        "/health",
    ];

    // public static Methods

    /// <summary>
    /// True when a user carrying <c>MustEnrolTotp</c> or <c>MustChangePassword</c> may still
    /// be served this path.
    /// </summary>
    public static bool IsAllowedWhilePendingObligations(string path) =>
        _allowedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
