using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Fakturenn.Web;

/// <summary>
/// Translates the ASP.NET Core Identity validation messages that this application can
/// actually produce.
/// <para>
/// Identity's own descriptions are hard-coded English. Without this type a German user who
/// chooses a short password is told "Passwords must be at least 12 characters." on a page
/// where everything else is German — and password validation is the framework text a user
/// is by far the most likely to meet, because it fires on first-run setup, on the forced
/// password change after an administrator creates an account, and on every administrative
/// reset.
/// </para>
/// <para>
/// <b>Only the reachable errors are overridden</b>, deliberately, rather than the whole
/// base class. Every method below corresponds to a call this application makes:
/// </para>
/// <list type="bullet">
///   <item>the six password rules, from <c>CreateAsync</c>, <c>ChangePasswordAsync</c> and
///   <c>ResetPasswordAsync</c>. <c>RequireNonAlphanumeric</c> is off by default but the
///   whole <c>Password</c> section is bound from the <c>Identity</c> configuration section
///   (see <see cref="IdentityConfiguration"/>), so a deployment can switch it on without a
///   rebuild;</item>
///   <item><see cref="DuplicateUserName"/> and <see cref="DuplicateEmail"/>, from
///   <c>/setup</c> and <c>/account/admin/create-user</c>. Both fire, and they fire
///   together, because the user name <i>is</i> the e-mail address here and
///   <c>RequireUniqueEmail</c> is on;</item>
///   <item><see cref="InvalidUserName"/> and <see cref="InvalidEmail"/>, from the same two
///   endpoints. <c>MudTextField</c>'s <c>Required</c> is component-level validation, not an
///   HTML <c>required</c> attribute, so an empty or malformed address reaches the server
///   validator from the real form, not only from a crafted post;</item>
///   <item><see cref="PasswordMismatch"/>, from <c>ChangePasswordAsync</c> when the current
///   password is wrong — the single most frequently seen of all of them;</item>
///   <item><see cref="InvalidToken"/>, from <c>ResetPasswordAsync</c> when the token
///   generated a line earlier no longer verifies;</item>
///   <item><see cref="UserLockoutNotEnabled"/>, from <c>/account/admin/set-lockout</c>,
///   which the endpoint already handles explicitly rather than swallowing.</item>
/// </list>
/// <para>
/// Everything else stays on the base implementation on purpose. <c>UserAlreadyHasPassword</c>
/// needs <c>AddPasswordAsync</c>, which nothing calls; <c>RecoveryCodeRedemptionFailed</c>
/// surfaces as a <c>SignInResult</c> and its description is never rendered; the role and
/// external-login errors have no user interface in this epic; and
/// <c>ConcurrencyFailure</c>/<c>DefaultError</c> describe a race or a defect, which is
/// operator-facing text and stays English for the same reason the logs do.
/// </para>
/// <para>
/// Registered scoped by <c>AddErrorDescriber</c>, and the culture is read at lookup time
/// rather than at construction, so the operator entrypoints — which run outside a request,
/// with no <c>Accept-Language</c> — keep getting English.
/// </para>
/// </summary>
/// <param name="localizer">The shared resource set, in the request's language.</param>
public sealed class LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer)
    : IdentityErrorDescriber
{
    // public Methods

    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), localizer["Identity_DuplicateEmail", email]);

    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), localizer["Identity_DuplicateUserName", userName]);

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), localizer["Identity_InvalidEmail", email ?? string.Empty]);

    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), localizer["Identity_InvalidUserName", userName ?? string.Empty]);

    public override IdentityError InvalidToken() =>
        Error(nameof(InvalidToken), localizer["Identity_InvalidToken"]);

    public override IdentityError PasswordMismatch() =>
        Error(nameof(PasswordMismatch), localizer["Identity_PasswordMismatch"]);

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), localizer["Identity_PasswordRequiresDigit"]);

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), localizer["Identity_PasswordRequiresLower"]);

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), localizer["Identity_PasswordRequiresNonAlphanumeric"]);

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), localizer["Identity_PasswordRequiresUniqueChars", uniqueChars]);

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), localizer["Identity_PasswordRequiresUpper"]);

    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), localizer["Identity_PasswordTooShort", length]);

    public override IdentityError UserLockoutNotEnabled() =>
        Error(nameof(UserLockoutNotEnabled), localizer["Identity_UserLockoutNotEnabled"]);

    // private Methods

    /// <summary>
    /// Builds the error with the <b>base class's own code</b>, never a code of our own.
    /// <c>POST /account/setup</c> branches on
    /// <c>error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)</c> to tell a
    /// duplicate first administrator apart from a rejected one, and that comparison is
    /// against the untranslated code — which is exactly why a code must never be
    /// translated, only its description.
    /// <para>
    /// Every key above is spelled out at its own call site rather than passed in as a
    /// variable, so <c>SharedResourceTests</c>'s source scan can see it. A key resolved
    /// indirectly would be invisible to that scan, and a typo in it would silently render
    /// the key name to the user instead of the sentence.
    /// </para>
    /// </summary>
    private static IdentityError Error(string code, string description) =>
        new() { Code = code, Description = description };
}
