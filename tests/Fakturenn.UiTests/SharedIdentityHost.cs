namespace Fakturenn.UiTests;

/// <summary>
/// One <see cref="AuthenticatedWebAppFixture"/> — and therefore one PostgreSQL container,
/// one host and one enrolled administrator — shared by every browser test whose subject is
/// an existing session.
/// <para>
/// A class fixture would start a container per test class, and the reusable authenticated
/// state SPIKE-009 asks about would be reusable only within a class. Sharing means the
/// classes in this collection run one after another rather than in parallel, which is the
/// price of the shared database; the tests do not depend on that order.
/// </para>
/// </summary>
[CollectionDefinition(nameof(SharedIdentityHost))]
public sealed class SharedIdentityHost : ICollectionFixture<AuthenticatedWebAppFixture>;
