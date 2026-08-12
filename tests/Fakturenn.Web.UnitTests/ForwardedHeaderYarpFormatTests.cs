using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// The RFC says what a <c>Forwarded</c> header may look like; a proxy decides what it
/// actually emits. YARP is the reverse proxy whose emitted forms are public and
/// documented, so it is the one worth measuring against. Two of its properties make
/// this more than a curiosity: enabling its <c>Forwarded</c> transform switches its
/// <c>X-Forwarded</c> transforms off, so there is no fallback header, and its
/// <c>ForFormat</c> defaults to <c>Random</c> — an obfuscated identifier, as RFC 7239
/// section 6.2 requires of a default.
/// <para>
/// These tests record what our parser does, not what would be convenient. Nothing here
/// justifies changing <see cref="ForwardedHeaderNormalizer"/>.
/// </para>
/// </summary>
public sealed class ForwardedHeaderYarpFormatTests
{
    /// <summary>
    /// Verbatim from YARP's request-transform documentation. Note what it exercises at
    /// once: one element with semicolon-separated parameters, <c>proto</c> and
    /// <c>host</c> ahead of <c>for</c>, a quoted bracketed IPv6 carrying a port, and a
    /// <c>by=</c> node we have no use for.
    /// </summary>
    private const string YarpDefaultHeader =
        "proto=https;host=\"localhost:5001\";for=\"[::1]:20173\";by=_YQuN68tm6";

    private static readonly IPAddress _proxy = IPAddress.Parse("203.0.113.7");

    [Fact]
    public async Task The_documented_YARP_header_yields_the_client_address()
    {
        HttpContext context = await RunAsync(YarpDefaultHeader);

        context.Request.Headers["X-Forwarded-For"].ToString().Should().Be(
            "[::1]",
            "the port is stripped and the bracketed literal kept, which is the form the "
                + "built-in middleware parses");
    }

    [Fact]
    public async Task The_documented_YARP_header_yields_the_scheme()
    {
        HttpContext context = await RunAsync(YarpDefaultHeader);

        context.Request.Headers["X-Forwarded-Proto"].ToString().Should().Be(
            "https",
            "proto arriving before for in the element must not depend on parameter order");
    }

    [Fact]
    public async Task The_documented_YARP_header_yields_the_host()
    {
        HttpContext context = await RunAsync(YarpDefaultHeader);

        context.Request.Headers["X-Forwarded-Host"].ToString().Should().Be(
            "localhost:5001",
            "the quotes are YARP's, because the value contains a colon; the host:port pair is the value");
    }

    [Fact]
    public async Task The_by_node_is_ignored()
    {
        HttpContext context = await RunAsync(YarpDefaultHeader);

        // by= identifies the proxy's own inbound interface. Trust is anchored on the
        // connection's peer address, so a self-reported by= adds nothing, and here it
        // is obfuscated anyway.
        context.Request.Headers["X-Forwarded-For"].ToString().Should().NotContain("_YQuN68tm6");
    }

    [Theory]
    // ForFormat=Random — YARP's default, and RFC 7239 section 6.2's.
    [InlineData("for=_YQuN68tm6", null)]
    // ForFormat=RandomAndPort / RandomAndRandomPort.
    [InlineData("for=\"_YQuN68tm6:80\"", null)]
    [InlineData("for=\"_YQuN68tm6:_jDw5Cf3tQ\"", null)]
    // ForFormat=Unknown / UnknownAndPort / UnknownAndRandomPort.
    [InlineData("for=unknown", null)]
    [InlineData("for=\"unknown:80\"", null)]
    [InlineData("for=\"unknown:_jDw5Cf3tQ\"", null)]
    // ForFormat=Ip, both address families.
    [InlineData("for=\"[::1]\"", "[::1]")]
    [InlineData("for=192.0.2.1", "192.0.2.1")]
    // ForFormat=IpAndPort, both families.
    [InlineData("for=\"[::1]:80\"", "[::1]")]
    [InlineData("for=\"192.0.2.1:80\"", "192.0.2.1")]
    // ForFormat=IpAndRandomPort: a real address whose *port* is an obfuscated token.
    // The port is discarded either way, so obfuscating it costs nothing.
    [InlineData("for=\"[::1]:_jDw5Cf3tQ\"", "[::1]")]
    [InlineData("for=\"192.0.2.1:_jDw5Cf3tQ\"", "192.0.2.1")]
    public async Task Each_YARP_ForFormat_is_translated_or_rejected(string header, string? expected)
    {
        HttpContext context = await RunAsync(header);

        if (expected is null)
        {
            context.Request.Headers.ContainsKey("X-Forwarded-For").Should().BeFalse(
                "an obfuscated or unknown node carries no address, and passing the token through "
                    + "would produce an X-Forwarded-For entry the built-in parser silently discards");
        }
        else
        {
            context.Request.Headers["X-Forwarded-For"].ToString().Should().Be(expected);
        }
    }

    [Fact]
    public async Task Forwarded_headers_from_successive_proxies_accumulate_into_one_chain()
    {
        // Each proxy in a chain appends its own header rather than editing the one it
        // received, so the app sees several Forwarded headers, client-nearest first.
        string[] chain =
        [
            "for=\"192.0.2.60:31337\";proto=https;host=\"invoices.example:443\";by=_edgeNode",
            "for=\"[2001:db8::1]:20173\";proto=http;by=_innerNode",
        ];

        HttpContext context = await RunAsync(chain);

        context.Request.Headers["X-Forwarded-For"].ToString().Should().Be("192.0.2.60, [2001:db8::1]");
        context.Request.Headers["X-Forwarded-Proto"].ToString().Should().Be(
            "https", "the client-nearest element wins, which is the first one");
        context.Request.Headers["X-Forwarded-Host"].ToString().Should().Be("invoices.example:443");
    }

    [Fact]
    public async Task A_request_carrying_only_Forwarded_is_honoured_end_to_end()
    {
        // The precedence rule is "X-Forwarded-For present means Forwarded is ignored".
        // Behind a proxy configured to emit Forwarded there is no X-Forwarded-For at
        // all, so the rule must not have quietly become a *requirement* for one.
        HttpContext context = await RunThroughForwardedHeadersMiddlewareAsync(
            "for=\"192.0.2.60:31337\";proto=https");

        context.Connection.RemoteIpAddress.Should().Be(
            IPAddress.Parse("192.0.2.60"),
            "the shim synthesised X-Forwarded-For and the built-in middleware then applied it");
        context.Request.Scheme.Should().Be("https");
    }

    [Fact]
    public async Task An_obfuscated_for_leaves_the_client_address_as_the_proxys_own()
    {
        // This is the operational consequence of YARP's default ForFormat=Random: no
        // address reaches us, no X-Forwarded-For is synthesised, and the built-in
        // middleware has nothing to apply. The peer address survives untouched.
        HttpContext context = await RunThroughForwardedHeadersMiddlewareAsync(
            "proto=https;host=\"invoices.example\";for=_YQuN68tm6;by=_edgeNode");

        context.Connection.RemoteIpAddress.Should().Be(
            _proxy,
            "an obfuscated node is not an address, so the client stays invisible to the application");
        context.Request.Scheme.Should().Be("https", "proto still arrives; only for= is obfuscated");
    }

    [Fact]
    public async Task Distinct_obfuscated_clients_collapse_into_one_rate_limiter_partition()
    {
        // The account rate limiter partitions on Connection.RemoteIpAddress (see
        // IdentityConfiguration). Two different clients behind a proxy emitting
        // ForFormat=Random are indistinguishable to it, so they share one partition and
        // one budget -- the self-DoS the limiter's own comment names, arrived at through
        // a *default* proxy configuration rather than a misconfigured one.
        HttpContext first = await RunThroughForwardedHeadersMiddlewareAsync("for=_YQuN68tm6");
        HttpContext second = await RunThroughForwardedHeadersMiddlewareAsync("for=_kP2xRb9La");

        first.Connection.RemoteIpAddress.Should().Be(second.Connection.RemoteIpAddress);
        first.Connection.RemoteIpAddress.Should().Be(_proxy);
    }

    [Fact]
    public async Task An_Ip_ForFormat_keeps_those_clients_apart()
    {
        // The refutation of the test above: with an address-bearing ForFormat the same
        // two clients partition separately, so the collapse is the format's doing and
        // not the shim's.
        HttpContext first = await RunThroughForwardedHeadersMiddlewareAsync("for=\"192.0.2.60:31337\"");
        HttpContext second = await RunThroughForwardedHeadersMiddlewareAsync("for=\"192.0.2.61:31338\"");

        first.Connection.RemoteIpAddress.Should().NotBe(second.Connection.RemoteIpAddress);
        first.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("192.0.2.60"));
        second.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("192.0.2.61"));
    }

    /// <summary>Runs the shim alone, so the assertions are about translation.</summary>
    private static async Task<HttpContext> RunAsync(params string[] forwarded)
    {
        ServiceCollection services = new();
#pragma warning disable ASP0000 // No hosted application here; ApplicationBuilder needs a provider.
        using ServiceProvider provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

        ApplicationBuilder app = new(provider);
        app.UseRfc7239Forwarded();
        app.Run(_ => Task.CompletedTask);

        DefaultHttpContext context = new();
        context.Request.Headers["Forwarded"] = forwarded;
        await app.Build()(context);
        return context;
    }

    /// <summary>
    /// Runs the shim followed by the built-in middleware, configured the way
    /// <see cref="ForwardedHeaderTrust"/> configures it, so the assertions are about
    /// what the rest of the application observes.
    /// </summary>
    private static async Task<HttpContext> RunThroughForwardedHeadersMiddlewareAsync(string forwarded)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Add(_proxy);
        });

#pragma warning disable ASP0000 // No hosted application here; ApplicationBuilder needs a provider.
        using ServiceProvider provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

        ApplicationBuilder app = new(provider);
        app.UseRfc7239Forwarded();
        app.UseForwardedHeaders();
        app.Run(_ => Task.CompletedTask);

        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = _proxy;
        context.Request.Scheme = "http";
        context.Request.Headers["Forwarded"] = forwarded;
        await app.Build()(context);
        return context;
    }
}
