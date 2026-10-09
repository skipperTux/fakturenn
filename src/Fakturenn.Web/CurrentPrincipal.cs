using System.Security.Claims;

namespace Fakturenn.Web;

/// <summary>
/// The principal an operation acts as, set by <see cref="CircuitOperations"/> on the fresh
/// scope it opens. Null on an ordinary HTTP request, where the request's own user applies.
/// </summary>
public sealed class CurrentPrincipal
{
    public ClaimsPrincipal? Principal { get; set; }
}
