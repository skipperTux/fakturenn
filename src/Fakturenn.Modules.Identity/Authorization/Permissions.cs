namespace Fakturenn.Modules.Identity.Authorization;

/// <summary>
/// The closed set of permissions this application enforces. Code authorizes on
/// these constants, never on a role name, so a role can be created or renamed by
/// an operator without a deploy while the set of things code checks stays fixed
/// and greppable.
/// </summary>
public static class Permissions
{
    // public const Fields
    /// <summary>Enforced on the user list at <c>GET /admin/users</c>.</summary>
    public const string UsersRead = "users.read";

    /// <summary>Enforced on every mutating administrative endpoint.</summary>
    public const string UsersManage = "users.manage";

    // public static readonly Fields
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UsersRead,
        UsersManage,
    };
}

/// <summary>The claim type carrying a granted permission.</summary>
public static class PermissionClaims
{
    public const string Type = "fakturenn.permission";
}
