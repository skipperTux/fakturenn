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

    [Fact]
    public async Task The_principal_is_gone_once_the_operation_returns()
    {
        await using ServiceProvider services = Services(new FixedAuthenticationStateProvider(User(Permissions.MasterDataManage)));
        await using AsyncServiceScope circuit = services.CreateAsyncScope();
        CircuitOperations operations = circuit.ServiceProvider.GetRequiredService<CircuitOperations>();

        await operations.RunAsync<Probe, string>(
            Permissions.MasterDataManage, (probe, _) => Task.FromResult(probe.Seen), TestContext.Current.CancellationToken);

        services.GetRequiredService<CurrentPrincipal>().Principal.Should().BeNull();
    }

    [Fact]
    public async Task Work_an_operation_started_does_not_act_as_its_user_after_it_returns()
    {
        // A Task.Run, a timer or a message loop started inside an operation copies its
        // execution context, and with it the ambient principal. Rows it writes later must not
        // carry the name of whoever happened to start it.
        await using ServiceProvider services = Services(new FixedAuthenticationStateProvider(User(Permissions.MasterDataManage)));
        await using AsyncServiceScope circuit = services.CreateAsyncScope();
        CircuitOperations operations = circuit.ServiceProvider.GetRequiredService<CircuitOperations>();
        TaskCompletionSource operationReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string>? startedInside = null;

        await operations.RunAsync<Probe, bool>(
            Permissions.MasterDataManage,
            (probe, _) =>
            {
                startedInside = Task.Run(async () =>
                {
                    await operationReturned.Task;
                    return probe.Seen;
                });
                return Task.FromResult(true);
            },
            TestContext.Current.CancellationToken);
        operationReturned.SetResult();

        (await startedInside!).Should().Be("nobody");
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
        services.AddSingleton<CurrentPrincipal>();
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
