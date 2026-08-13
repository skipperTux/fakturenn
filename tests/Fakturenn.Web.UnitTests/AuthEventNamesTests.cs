using System.Reflection;
using AwesomeAssertions;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// The authentication event names are a contract: an operator writes alerting rules against
/// them, and a rule that names an event nothing emits fires never rather than loudly.
/// <para>
/// These tests read <c>Fakturenn.Web.Logging.AuthEvents</c> by reflection because the class
/// is <c>internal</c> — deliberately, per this repository's "public only where another
/// assembly genuinely consumes it". A test is not a consumer worth widening the API surface
/// for, and reading the metadata is strictly stronger than compiling against it: the
/// enumeration below sees every constant that exists, including one added without a thought
/// for this file.
/// </para>
/// </summary>
public sealed class AuthEventNamesTests
{
    /// <summary>
    /// Every event this application emits. Adding an event means adding a line here, on
    /// purpose — the point is that the set cannot grow or shrink unnoticed.
    /// </summary>
    private static readonly string[] _expected =
    [
        // Sign-in and self-service.
        "SignInSucceeded",
        "SignInFailed",
        "AccountLockedOut",
        "TwoFactorSucceeded",
        "TwoFactorFailed",
        "RecoveryCodeUsed",
        "RecoveryCodeFailed",
        "TotpEnrolled",
        "PasswordChanged",
        "SignedOut",
        "FirstAdministratorCreated",

        // Administrative actions.
        "AdminCreatedUser",
        "AdminResetPassword",
        "AdminClearedMfa",
        "AdminLockedUser",
        "AdminUnlockedUser",

        // Command-line recovery entrypoints.
        "OperatorCreatedAdmin",
        "OperatorResetPassword",
        "OperatorResetMfa",
        "OperatorUnlockedUser",
    ];

    [Fact]
    public void The_event_names_are_exactly_the_expected_set()
    {
        Values().Should().BeEquivalentTo(
            _expected,
            "an event added or removed without noticing silently changes what an operator's "
            + "alerting rules can match");
    }

    [Fact]
    public void Every_constant_names_itself()
    {
        // A constant whose value differs from its field name is the failure this class
        // exists to prevent, moved one level up: the call site would read correctly and the
        // log would carry something else.
        foreach (FieldInfo field in Constants())
        {
            field.GetRawConstantValue().Should().Be(field.Name);
        }
    }

    private static IEnumerable<string> Values() =>
        Constants().Select(field => (string)field.GetRawConstantValue()!);

    private static FieldInfo[] Constants()
    {
        Type events = Type.GetType("Fakturenn.Web.Logging.AuthEvents, Fakturenn.Web")
            ?? throw new InvalidOperationException(
                "Fakturenn.Web.Logging.AuthEvents was not found. Renaming it breaks every call site "
                + "at compile time, so a failure here means the type moved rather than that it vanished.");

        FieldInfo[] constants = [.. events
            .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false })];

        constants.Should().NotBeEmpty("reflection that finds nothing would make every assertion vacuous");

        return constants;
    }
}
