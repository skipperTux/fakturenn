using System.Security.Claims;

namespace Fakturenn.Web;

/// <summary>
/// The principal an operation acts as, set by <see cref="CircuitOperations"/> for the length
/// of one operation. Null on an ordinary HTTP request, where the request's own user applies.
/// <para>
/// Ambient, and built the way <see cref="HttpContextAccessor"/> is: the async-local holds a
/// holder rather than the principal. Every flow started inside an operation — a
/// <c>Task.Run</c>, a timer, a message loop — copies the execution context and so shares the
/// holder; clearing the holder at the end of the operation clears it for all of them, where
/// resetting the async-local would reach only the flow that does it.
/// </para>
/// <para>
/// A per-scope holder would be simpler, but the audit interceptor of InvoicesDbContext is
/// resolved from the root provider (see IdentityConfiguration), and a scoped value is
/// invisible from there.
/// </para>
/// </summary>
public sealed class CurrentPrincipal
{
    // private readonly Fields
    private readonly AsyncLocal<Holder?> _current = new();

    // public Properties
    public ClaimsPrincipal? Principal
    {
        get => _current.Value?.Principal;
        set
        {
            if (_current.Value is Holder previous)
            {
                previous.Principal = null;
            }

            if (value is not null)
            {
                _current.Value = new Holder { Principal = value };
            }
        }
    }

    // private Types
    private sealed class Holder
    {
        public ClaimsPrincipal? Principal { get; set; }
    }
}
