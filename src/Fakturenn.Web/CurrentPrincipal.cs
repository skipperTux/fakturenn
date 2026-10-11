using System.Security.Claims;

namespace Fakturenn.Web;

/// <summary>
/// The principal an operation acts as, set by <see cref="CircuitOperations"/> before it runs
/// one. Null on an ordinary HTTP request, where the request's own user applies.
/// <para>
/// Ambient, the way <see cref="IHttpContextAccessor"/> is: the value flows with the
/// operation's async calls and goes back when the operation returns. A per-scope holder would
/// be simpler, but the audit interceptor of InvoicesDbContext is resolved from the root
/// provider (see IdentityConfiguration), and a scoped value is invisible from there.
/// </para>
/// </summary>
public sealed class CurrentPrincipal
{
    // private readonly Fields
    private readonly AsyncLocal<ClaimsPrincipal?> _principal = new();

    // public Properties
    public ClaimsPrincipal? Principal
    {
        get => _principal.Value;
        set => _principal.Value = value;
    }
}
