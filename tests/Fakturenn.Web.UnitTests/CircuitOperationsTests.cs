using System.Security.Claims;
using AwesomeAssertions;
using Fakturenn.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.Web.UnitTests;

public sealed class CircuitOperationsTests
{
    // public Methods
    [Fact]
    public async Task An_operation_runs_when_the_current_user_holds_the_permission()
    {
        await using ServiceProvider services = Services(new FixedAuthenticationStateProvider(User(Permissions.MasterDataManage)));
        await using AsyncServiceScope circuit = services.CreateAsyncScope();
        CircuitOperations operations = circuit.ServiceProvider.GetRequiredService<CircuitOperations>();

        string ran = await operations.RunAsync<Probe, string>(
            Permissions.MasterDataManage, (probe, _) => Task.FromResult(probe.Seen), TestContext.Current.CancellationToken);

        ran.Should().Be("user@example.test", "the operation's scope carries the circuit's current user");
    }

    [Fact]
    public async Task An_operation_is_refused_without_the_permission()
    {
        await using ServiceProvider services = Services(new FixedAuthenticationStateProvider(User(Permissions.UsersRead)));
        await using AsyncServiceScope circuit = services.CreateAsyncScope();
        CircuitOperations operations = circuit.ServiceProvider.GetRequiredService<CircuitOperations>();

        Func<Task> act = () => operations.RunAsync<Probe, string>(
            Permissions.MasterDataManage, (probe, _) => Task.FromResult(probe.Seen), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationRefusedException>();
    }

    [Fact]
    public async Task An_operation_is_refused_once_the_state_turns_anonymous()
    {
        // What revalidation does to a locked user's circuit: the provider's state becomes
        // anonymous mid-circuit, and the very next operation must see it -- a CircuitOperations
        // that cached the first state would keep running.
        FixedAuthenticationStateProvider provider = new(User(Permissions.MasterDataManage));
        await using ServiceProvider services = Services(provider);
        await using AsyncServiceScope circuit = services.CreateAsyncScope();
        CircuitOperations operations = circuit.ServiceProvider.GetRequiredService<CircuitOperations>();
        await operations.RunAsync<Probe, string>(
            Permissions.MasterDataManage, (probe, _) => Task.FromResult(probe.Seen), TestContext.Current.CancellationToken);

        provider.SetUser(new ClaimsPrincipal(new ClaimsIdentity()));
        Func<Task> act = () => operations.RunAsync<Probe, string>(
            Permissions.MasterDataManage, (probe, _) => Task.FromResult(probe.Seen), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationRefusedException>();
    }

    // private static Methods
    private static ClaimsPrincipal User(string permission) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "user@example.test"), new Claim(PermissionClaims.Type, permission)],
            authenticationType: "test"));

    private static ServiceProvider Services(FixedAuthenticationStateProvider provider)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<AuthenticationStateProvider>(_ => provider);
        services.AddScoped<CurrentPrincipal>();
        services.AddScoped<CircuitOperations>();
        services.AddScoped<Probe>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    // private Types
    private sealed class Probe(CurrentPrincipal current)
    {
        public string Seen => current.Principal?.Identity?.Name ?? "nobody";
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = user;

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(_user));

        public void SetUser(ClaimsPrincipal user)
        {
            _user = user;
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }
}
