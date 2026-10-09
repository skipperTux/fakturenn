using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace Fakturenn.Web;

/// <summary>
/// The one way an interactive page touches a module. Two jobs, both forced by circuits:
/// <list type="bullet">
/// <item>The permission is checked against the circuit's <b>current</b> authentication
/// state, not against what the page was rendered with. A circuit makes no new HTTP
/// request, so the page attribute is evaluated once; revalidation turns the state
/// anonymous when the security stamp changes, and this is where that takes effect
/// (spec section 8, review S3/S4).</item>
/// <item>Every operation gets a fresh DI scope and therefore a fresh <c>DbContext</c>. A
/// context injected into a component would live as long as the tab, track everything it
/// ever loaded and be shared by overlapping event handlers (review C1). A scope rather
/// than <c>IDbContextFactory</c>: Wolverine refuses handlers whose only route to a context
/// is a factory, and these become handlers once Stage 2 publishes.</item>
/// </list>
/// </summary>
public sealed class CircuitOperations(
    AuthenticationStateProvider authenticationState,
    IAuthorizationService authorization,
    IServiceScopeFactory scopes)
{
    // public Methods
    public async Task<TResult> RunAsync<TService, TResult>(
        string permission,
        Func<TService, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);

        AuthenticationState state = await authenticationState.GetAuthenticationStateAsync();
        AuthorizationResult result = await authorization.AuthorizeAsync(state.User, resource: null, permission);
        if (!result.Succeeded)
        {
            throw new OperationRefusedException(permission);
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentPrincipal>().Principal = state.User;
        return await operation(scope.ServiceProvider.GetRequiredService<TService>(), cancellationToken);
    }

    public Task RunAsync<TService>(
        string permission,
        Func<TService, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunAsync<TService, bool>(
            permission,
            async (service, token) =>
            {
                await operation(service, token);
                return true;
            },
            cancellationToken);
    }
}
