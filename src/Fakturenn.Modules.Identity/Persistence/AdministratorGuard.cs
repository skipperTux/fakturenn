namespace Fakturenn.Modules.Identity.Persistence;

/// <summary>
/// Stops the user interface locking an instance out of its own administration.
/// The CLI entrypoints remain the escape hatch if it happens anyway.
/// </summary>
public static class AdministratorGuard
{
    public static bool WouldRemoveLastAdministrator(int administratorCount, bool targetIsAdministrator) =>
        targetIsAdministrator && administratorCount <= 1;
}
