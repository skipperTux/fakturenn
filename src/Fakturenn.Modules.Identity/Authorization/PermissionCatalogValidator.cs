namespace Fakturenn.Modules.Identity.Authorization;

/// <summary>
/// Compares permissions stored against roles with the closed set the code defines.
/// A stored value the code does not know grants nothing, and silently granting
/// nothing looks exactly like a working configuration until someone is denied.
/// <para>
/// Comparison is ordinal, matching <c>PermissionAuthorizationHandler</c>. The two
/// mechanisms have to agree about the same string: a case-insensitive validator
/// would report a row reading <c>Users.Manage</c> as healthy while the handler
/// grants nothing for it.
/// </para>
/// <para>
/// Lives beside <see cref="Permissions"/> rather than under <c>Persistence</c>: it is
/// a pure function over the catalogue and touches no database. Putting it in
/// <c>Persistence</c> made that namespace depend on <c>Authorization</c> while
/// <c>Authorization</c> depended back on <c>Persistence</c>, which the slice cycle
/// rule rejected.
/// </para>
/// </summary>
public static class PermissionCatalogValidator
{
    public static IReadOnlyList<string> FindUnknownPermissions(IEnumerable<string> stored) =>
        [.. stored.Where(permission => !Permissions.All.Contains(permission)).Distinct(StringComparer.Ordinal)];
}
