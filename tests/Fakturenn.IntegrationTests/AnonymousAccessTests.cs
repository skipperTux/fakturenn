using System.Net;
using AwesomeAssertions;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// What a signed-out visitor may reach under the authenticated-by-default fallback policy,
/// and nothing more. Each row is a path that worked before the fallback policy and must
/// keep working. Of the refusals, only the circuit proves the fallback itself:
/// <c>/admin/users</c> and <c>/account/change-password</c> were already closed by their own
/// <c>[Authorize]</c> attributes, and stay in as regression rows.
/// </summary>
[Collection(RealHost.Name)]
public sealed class AnonymousAccessTests(SetupHostFixture host)
{
    // public Methods
    [Theory]
    [InlineData("/")]
    [InlineData("/setup")]
    [InlineData("/account/login")]
    [InlineData("/account/login-2fa")]
    [InlineData("/account/login-recovery")]
    [InlineData("/account/lockout")]
    [InlineData("/account/denied")]
    [InlineData("/alive")]
    // /_framework/blazor.web.js is absent on purpose: this host is not Development, so the
    // file does not exist here and the fallback policy answers 302. It is tested where the
    // file exists: AuthorizationJourneyTests in Fakturenn.UiTests.
    public async Task A_signed_out_visitor_reaches(string path)
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        // A page may redirect on its own (the sign-in page sends an instance with no user
        // to /setup); only the fallback policy's challenge carries a ReturnUrl.
        response.Headers.Location?.OriginalString.Should().NotContain("ReturnUrl",
            $"{path} must not challenge a signed-out visitor");
        ((int)response.StatusCode).Should().BeLessThan(400, $"{path} must answer a signed-out visitor");
    }

    [Fact]
    public async Task A_signed_out_probe_reaches_the_readiness_check()
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        // 200 or 503 depending on the database; never a redirect to a sign-in page a probe
        // cannot follow.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/admin/users")]
    [InlineData("/account/change-password")]
    public async Task A_signed_out_visitor_is_sent_to_sign_in(string path)
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.AbsolutePath.Should().Be("/account/login");
    }

    [Fact]
    public async Task A_signed_out_visitor_cannot_open_a_circuit()
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(
            "/_blazor/negotiate?negotiateVersion=1", content: null, TestContext.Current.CancellationToken);

        // Measured: the cookie handler answers 401 rather than 302 for a SignalR negotiate request.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the circuit endpoint carries no AllowAnonymous, so the fallback policy challenges it");
    }
}
