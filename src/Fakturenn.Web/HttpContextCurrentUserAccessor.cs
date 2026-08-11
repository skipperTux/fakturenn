using System.Security.Claims;
using Fakturenn.SharedKernel;

namespace Fakturenn.Web;

/// <summary>
/// Resolves the acting user from the current request's principal, for
/// <c>AuditSaveChangesInterceptor</c>.
/// <para>
/// It lives in the host rather than in the shared kernel because it needs
/// <see cref="IHttpContextAccessor"/>, and the shared kernel is referenced by the
/// <c>.Contracts</c> assemblies that form the cross-module surface — it deliberately
/// carries no package or project references at all. It is not a module's concern
/// either: every module's rows are stamped by the same accessor.
/// </para>
/// <para>
/// Null is the contract, not a gap. <c>AuditStamp.ResolveUser</c> owns the mapping
/// from "nobody was acting" to the <c>system</c> actor; repeating it here would give
/// one decision two homes that can drift apart.
/// </para>
/// </summary>
public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserAccessor
{
    public string? UserName
    {
        get
        {
            ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;

            // IsAuthenticated is the load-bearing half. An anonymous ClaimsIdentity
            // still exposes a readable Name, so reading the name alone would attribute
            // an unauthenticated request to whatever name it happened to carry.
            return user?.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
        }
    }
}
