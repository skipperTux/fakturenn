namespace Fakturenn.SharedKernel;

/// <summary>
/// The signed-in user's name, or null when there is no request — migrations,
/// seeding, background work and the operator entrypoints all run without one.
/// <para>
/// An abstraction rather than <c>IHttpContextAccessor</c> so that the shared kernel
/// stays free of ASP.NET Core, and so the claim actually consulted can change in one
/// place when generic OIDC eventually lands.
/// </para>
/// </summary>
public interface ICurrentUserAccessor
{
    string? UserName { get; }
}
