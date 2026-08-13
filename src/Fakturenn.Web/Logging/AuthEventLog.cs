namespace Fakturenn.Web.Logging;

/// <summary>
/// Emits the authentication events named by <see cref="AuthEvents"/>, so an operator can
/// answer "is someone attacking this instance" from the log alone.
/// <para>
/// This is <b>not</b> the Audit module, which owns <c>AuditEvent</c> as domain data, and not
/// row provenance, which records who changed a row and says nothing about a failed attempt
/// that changed nothing.
/// </para>
/// <para>
/// Source-generated <c>[LoggerMessage]</c> delegates rather than <c>ILogger.LogInformation</c>
/// calls, because CA1848 and CA1873 are build errors in this repository — the same reason
/// <c>DatabaseMigrator</c> and <c>ForwardedHeaderTrust</c> are partial classes.
/// </para>
/// <para>
/// Four shapes rather than one method per event, with the event name passed as
/// <c>{Event}</c>: the name has to be a structured property so a query can select on it
/// without depending on message wording, and <c>[LoggerMessage]</c> needs the template to
/// be a compile-time constant. The name always comes from <see cref="AuthEvents"/>; no call
/// site passes a literal.
/// </para>
/// <para>
/// <b>What must never appear here.</b> No password, TOTP code, recovery code, authenticator
/// key, password-reset token, security stamp or Data Protection payload — an error message
/// that helpfully echoes the input is how secrets reach a log aggregator. No cookie value or
/// session identifier either: a reader of the log must not be able to reconstruct a session
/// from it. E-mail addresses <i>are</i> logged, deliberately — an operator cannot act on an
/// incident without knowing which account it concerns.
/// </para>
/// </summary>
internal static partial class AuthEventLog
{
    /// <summary>
    /// The logger category every authentication event is written under, web endpoints and
    /// command-line entrypoints alike — so one filter finds an account's whole history
    /// whether it was touched through a page or from a shell.
    /// <para>
    /// A fixed name rather than <c>ILogger&lt;T&gt;</c>, which cannot name a static class,
    /// and rather than a category derived from whichever class happens to hold the call
    /// site, which would change under refactoring. An operator configures minimum levels
    /// against this name, so it is chosen rather than inherited.
    /// </para>
    /// </summary>
    internal const string Category = "Fakturenn.Auth";

    // internal static Methods

    // Each wrapper resolves the logger into a local before calling the generated delegate.
    // Inlining the call as an argument is an error here: CA1873 refuses any argument to a
    // logging method that is itself a method call, because it would be evaluated even when
    // the level is disabled.

    /// <summary>An account-level event that went the way it was meant to.</summary>
    internal static void Account(ILoggerFactory factory, string @event, string email)
    {
        ILogger logger = LoggerFor(factory);

        Account(logger, @event, email);
    }

    /// <summary>
    /// An account-level event worth an operator's attention: a refused sign-in, a locked
    /// account, a spent recovery code.
    /// </summary>
    internal static void AccountAlert(ILoggerFactory factory, string @event, string email)
    {
        ILogger logger = LoggerFor(factory);

        AccountAlert(logger, @event, email);
    }

    /// <summary>
    /// An administrator acting on somebody else's account. Both identities are recorded:
    /// "who gave this account administrator access, and when" has no answer without the actor.
    /// </summary>
    internal static void Administrative(ILoggerFactory factory, string @event, string actor, string target)
    {
        ILogger logger = LoggerFor(factory);

        Administrative(logger, @event, actor, target);
    }

    /// <summary>
    /// A command-line recovery entrypoint. There is no actor to name — the authority was a
    /// shell on the host — so the affected account is the whole record.
    /// </summary>
    internal static void Operator(ILoggerFactory factory, string @event, string target)
    {
        ILogger logger = LoggerFor(factory);

        Operator(logger, @event, target);
    }

    // private static Methods

    /// <summary>
    /// <see cref="ILoggerFactory.CreateLogger"/> caches by name, so resolving the logger per
    /// call costs a dictionary lookup rather than an allocation.
    /// </summary>
    private static ILogger LoggerFor(ILoggerFactory factory) => factory.CreateLogger(Category);

    [LoggerMessage(Level = LogLevel.Information, Message = "AuthEvent {Event} {Email}")]
    private static partial void Account(ILogger logger, string @event, string email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AuthEvent {Event} {Email}")]
    private static partial void AccountAlert(ILogger logger, string @event, string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "AuthEvent {Event} {Actor} {Target}")]
    private static partial void Administrative(ILogger logger, string @event, string actor, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "AuthEvent {Event} {Target}")]
    private static partial void Operator(ILogger logger, string @event, string target);
}
