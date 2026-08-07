using Fakturenn.Web;
using Microsoft.AspNetCore.Builder;

namespace Fakturenn.UiTests;

/// <summary>
/// Hosts the real application on a real socket. Blazor Interactive Server needs
/// a WebSocket circuit, which an in-memory test server cannot provide, and port
/// 0 lets the OS pick a free port so parallel runs do not collide.
/// </summary>
public sealed class WebAppFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public string BaseAddress { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _app = FakturennWebApplication.Build(["--urls", "http://127.0.0.1:0"]);

        await _app.StartAsync();

        BaseAddress = _app.Urls.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
