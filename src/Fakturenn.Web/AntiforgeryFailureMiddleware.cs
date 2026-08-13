using Fakturenn.Web.Components.Account;
using Fakturenn.Web.Logging;
using Microsoft.AspNetCore.Antiforgery;

namespace Fakturenn.Web;

/// <summary>
/// Answers a refused antiforgery token with the form again, rather than with an exception page.
/// <para>
/// <b>The defect this exists for.</b> Every handler in <see cref="AccountEndpoints"/> binds an
/// <c>IFormCollection</c> parameter, which is what attaches antiforgery validation to it. When
/// validation fails, <c>RequestDelegateFactory</c> throws a <c>BadHttpRequestException</c>
/// wrapping the <c>AntiforgeryValidationException</c>, and nothing in this pipeline handles it.
/// Measured against a real instance rather than reasoned about, because the answer differs by
/// observer: under Development the request answers <c>400</c> carrying the developer exception
/// page's body, while <c>UseSerilogRequestLogging</c> — which catches, logs and rethrows before
/// that page ever sees it — records the request as an unhandled <c>500</c>. Under Production
/// there is no exception page and the response is a bare <c>400</c> with no body at all. The
/// operator reads "responded 500" and the user gets a stack trace or a blank page; neither is
/// an answer a person can act on.
/// </para>
/// <para>
/// <b>Why a middleware and not a try/catch.</b> <c>UseAntiforgery</c> validates the token
/// itself and records the outcome in <see cref="IAntiforgeryValidationFeature"/> before
/// calling the next middleware — it does not reject. Running immediately behind it means the
/// failure is read from that feature and the request short-circuits into a redirect, so the
/// exception is never thrown at all. Catching the exception instead would mean depending on
/// the wording and the type <c>RequestDelegateFactory</c> happens to throw.
/// </para>
/// <para>
/// <b>It does not weaken the check.</b> The post is still refused: nothing downstream runs, no
/// handler sees the request, and the response is a redirect to a page that renders a fresh
/// token. Only the shape of the refusal changes.
/// </para>
/// </summary>
internal sealed class AntiforgeryFailureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Absent on every request whose endpoint has no antiforgery metadata, which is every
        // GET and every endpoint outside the account group.
        if (context.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: false })
        {
            await next(context);
            return;
        }

        string? location = AccountForms.Expired(context);
        if (location is null)
        {
            // Not one of this application's forms, so there is no page to send the caller
            // back to. Leave the request to the endpoint and its 400 -- redirecting an
            // unknown poster to a sign-in page would be a worse answer than the honest
            // status code, and inventing one here would hide a missing AccountForms entry.
            await next(context);
            return;
        }

        // The one line that keeps a refused token visible. Before this middleware existed the
        // failure at least surfaced as a logged unhandled exception; a redirect that logged
        // nothing would make the application's cross-site defence silent exactly when it
        // fires. Warning, alongside the other refused-credential events.
        AuthEventLog.AccountAlert(
            loggerFactory,
            AuthEvents.AntiforgeryRejected,
            context.User.Identity?.Name ?? "unknown");

        context.Response.Redirect(location);
    }
}
