namespace Fakturenn.IntegrationTests;

/// <summary>
/// Every test class that drives the real host over HTTP shares one
/// <see cref="SetupHostFixture"/>.
/// <para>
/// A class fixture would start a second PostgreSQL container and a second host per
/// class. It would also let the classes run in parallel, and
/// <see cref="SetupHostFixture.ResetUsersAsync"/> deletes every user — one class would
/// delete the other's signed-in user mid-request. A collection gives one container and
/// serialises the classes within it.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class RealHost : ICollectionFixture<SetupHostFixture>
{
    public const string Name = "Fakturenn host";
}
