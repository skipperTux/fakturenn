namespace Fakturenn.Web.Logging;

/// <summary>
/// The stable name of every authentication event this application emits, in one place.
/// <para>
/// One class of constants rather than a string literal at each call site, because a
/// literal typo is invisible: an operator grepping their aggregator for
/// <c>SignInFailed</c> would silently miss every <c>SigninFailed</c>, and the log would
/// look complete while the alert never fired. The names are also the query keys an
/// operator writes rules against, so they are a contract — renaming one breaks a
/// deployment's saved searches, and adding one is how this list is meant to grow.
/// </para>
/// <para>
/// <c>AuthEventNamesTests</c> asserts the exact set, so an event added or renamed without
/// noticing fails the build rather than quietly changing the contract.
/// </para>
/// </summary>
internal static class AuthEvents
{
    // Sign-in and self-service, logged with the account's e-mail address.

    /// <summary>A password-only sign-in completed. The account has no second factor yet.</summary>
    internal const string SignInSucceeded = "SignInSucceeded";

    /// <summary>
    /// A password sign-in was refused. Deliberately carries <b>no</b> reason: Task 11 proved
    /// the endpoint answers identically for an unknown account and a wrong password, and a
    /// log that distinguished them would reintroduce the enumeration oracle for anyone who
    /// can read it.
    /// </summary>
    internal const string SignInFailed = "SignInFailed";

    /// <summary>A sign-in attempt met a locked account, whether locked by an administrator or by failures.</summary>
    internal const string AccountLockedOut = "AccountLockedOut";

    internal const string TwoFactorSucceeded = "TwoFactorSucceeded";

    internal const string TwoFactorFailed = "TwoFactorFailed";

    /// <summary>A recovery code was redeemed, which spends it. Warning: it is the exceptional path.</summary>
    internal const string RecoveryCodeUsed = "RecoveryCodeUsed";

    /// <summary>
    /// A recovery code was refused.
    /// <para>
    /// The one failure path that would otherwise be invisible, and the one where invisibility
    /// costs most: recovery codes are ten single-use credentials with no counter of their own
    /// beyond the shared account lockout, so somebody working through the space would leave no
    /// trace while every other refused credential in this application is logged.
    /// </para>
    /// <para>
    /// Carries neither the code that was tried — a credential — nor how many codes remain,
    /// which would tell a reader how much of the space is still worth guessing.
    /// </para>
    /// </summary>
    internal const string RecoveryCodeFailed = "RecoveryCodeFailed";

    internal const string TotpEnrolled = "TotpEnrolled";

    internal const string PasswordChanged = "PasswordChanged";

    internal const string SignedOut = "SignedOut";

    /// <summary>
    /// The first administrator was created through <c>/setup</c>. The most security-significant
    /// single event this application can emit: it is the one moment an unauthenticated caller
    /// mints administrative access.
    /// </summary>
    internal const string FirstAdministratorCreated = "FirstAdministratorCreated";

    // Administrative actions, logged with the acting administrator and the affected account.

    internal const string AdminCreatedUser = "AdminCreatedUser";

    internal const string AdminResetPassword = "AdminResetPassword";

    internal const string AdminClearedMfa = "AdminClearedMfa";

    internal const string AdminLockedUser = "AdminLockedUser";

    /// <summary>
    /// Present so unlocking is not invisible. An operator answering "who gave this account
    /// access back, and when" needs both edges of the lock, not only the one that took it away.
    /// </summary>
    internal const string AdminUnlockedUser = "AdminUnlockedUser";

    // Operator entrypoints. No actor: the authority came from a shell on the host, so there
    // is no authenticated identity to name -- which is exactly why these are worth logging.

    internal const string OperatorCreatedAdmin = "OperatorCreatedAdmin";

    internal const string OperatorResetPassword = "OperatorResetPassword";

    internal const string OperatorResetMfa = "OperatorResetMfa";

    internal const string OperatorUnlockedUser = "OperatorUnlockedUser";
}
