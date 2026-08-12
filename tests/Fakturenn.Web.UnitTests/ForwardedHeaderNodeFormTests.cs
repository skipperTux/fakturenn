using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// RFC 7239 says what a <c>Forwarded</c> header <em>may</em> look like; a proxy decides
/// what it actually emits. Of the widely deployed open-source proxies, HAProxy 2.8 and
/// later is the only first-class emitter — <c>option forwarded</c>, whose bare form
/// expands to <c>proto for</c> and puts a real address in <c>for=</c>. nginx,
/// ingress-nginx, Traefik, Caddy, Apache httpd and Envoy do not emit the header
/// first-class, and no proxy consumes it for its own client-IP decision. So HAProxy's
/// output is the realistic anchor here.
/// <para>
/// The RFC's node grammar (<c>nodename [":" node-port]</c>, where either half may be an
/// obfuscated token) admits more shapes than that one default, and HAProxy's own
/// <c>for-expr</c> and <c>for_port</c> options reach them, so every node form is covered
/// rather than only the default one. YARP appears twice as a deliberate counter-example:
/// it is a plausible thing to put in front of a .NET application and its <c>ForFormat</c>
/// defaults to <c>Random</c>, an obfuscated identifier.
/// </para>
/// <para>
/// These tests record what our parser does, not what would be convenient. Nothing here
/// justifies changing <see cref="ForwardedHeaderNormalizer"/>.
/// </para>
/// </summary>
public sealed class ForwardedHeaderNodeFormTests
{
    /// <summary>
    /// What HAProxy's bare <c>option forwarded</c> puts on the wire, verbatim. Two
    /// properties matter operationally: the default carries a real address rather than
    /// an obfuscated token, and <c>option forwarded</c> is independent of
    /// <c>option forwardfor</c> — enabling only the standards-compliant one sends no
    /// <c>X-Forwarded-*</c> at all.
    /// </summary>
    private const string HaproxyDefaultHeader = "proto=http;for=127.0.0.1";

    /// <summary>
    /// From YARP's request-transform documentation. One element exercising several
    /// things at once: a quoted <c>host</c> whose value contains a colon, a quoted
    /// bracketed IPv6 carrying a port, <c>proto</c> and <c>host</c> ahead of <c>for</c>,
    /// and an obfuscated <c>by=</c> node we have no use for.
    /// </summary>
    private const string YarpDocumentedHeader =
        "proto=https;host=\"localhost:5001\";for=\"[::1]:20173\";by=_YQuN68tm6";

    private static readonly IPAddress _proxy = IPAddress.Parse("203.0.113.7");

    [Fact]
    public async Task The_HAProxy_default_element_yields_the_client_address_and_scheme()
    {
        HttpContext context = await RunAsync(HaproxyDefaultHeader);

        context.Request.Headers["X-Forwarded-For"].ToString().Should().Be("127.0.0.1");
        context.Request.Headers["X-Forwarded-Proto"].ToString().Should().Be("http");
        context.Request.Headers.ContainsKey("X-Forwarded-Host").Should().BeFalse(
            "the bare option expands to `proto for`, so no host= is emitted to translate");
    }

    [Fact]
    public async Task The_documented_YARP_element_is_translated_and_its_by_node_ignored()
    {
        HttpContext context = await RunAsync(YarpDocumentedHeader);

        context.Request.Headers["X-Forwarded-For"].ToString().Should().Be(
            "[::1]",
            "the port is stripped and the bracketed literal kept, which is the form the "
                + "built-in middleware parses");
        context.Request.Headers["X-Forwarded-Proto"].ToString().Should().Be(
            "https",
            "proto arriving before for in the element must not depend on parameter order");
        context.Request.Headers["X-Forwarded-Host"].ToString().Should().Be(
            "localhost:5001",
            "the quotes are the emitter's, because the value contains a colon; the host:port pair is the value");

        // by= identifies the proxy's own inbound interface. Trust is anchored on the
        // connection's peer address, so a self-reported by= adds nothing, and here it
        // is obfuscated anyway.
        context.Request.Headers["X-Forwarded-For"].ToString().Should().NotContain("_YQuN68tm6");
    }

    [Theory]
    // nodename = IPv6address, the address family HAProxy's default emits for an IPv6 client.
    [InlineData("for=\"[2001:db8::1]\"", "[2001:db8::1]")]
    // nodename ":" node-port — reachable through HAProxy's for_port option.
    [InlineData("for=\"192.0.2.60:31337\"", "192.0.2.60")]
    [InlineData("for=\"[2001:db8::1]:31337\"", "[2001:db8::1]")]
    // A real address whose *port* is an obfuscated token (RFC 7239 section 6.3; YARP's
    // IpAndRandomPort). The port is discarded either way, so obfuscating it costs nothing.
    [InlineData("for=\"192.0.2.60:_jDw5Cf3tQ\"", "192.0.2.60")]
    [InlineData("for=\"[2001:db8::1]:_jDw5Cf3tQ\"", "[2001:db8::1]")]
    // Obfuscated nodename (section 6.3) — what RFC 7239 section 8.3 recommends as a
    // default configuration, and what YARP's ForFormat=Random does.
    [InlineData("for=_YQuN68tm6", null)]
    [InlineData("for=\"_YQuN68tm6:_jDw5Cf3tQ\"", null)]
    // The literal "unknown" nodename (section 6.2), with and without a port.
    [InlineData("for=unknown", null)]
    [InlineData("for=\"unknown:31337\"", null)]
    public async Task Each_RFC_7239_node_form_is_translated_or_rejected(string header, string? expected)
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
        // A proxy emitting Forwarded need not emit X-Forwarded-For beside it -- HAProxy's
        // option forwarded is independent of option forwardfor, and YARP's Forwarded
        // transform switches its X-Forwarded transforms off -- so the rule must not have
        // quietly become a *requirement* for one.
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
        // The operational consequence of an obfuscated node: no address reaches us, no
        // X-Forwarded-For is synthesised, and the built-in middleware has nothing to
        // apply. The peer address survives untouched. Whether an operator meets this
        // depends on the proxy -- HAProxy's default carries a real address, YARP's
        // ForFormat defaults to Random -- so it is a configuration hazard, not a
        // property of the header.
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
        // IdentityConfiguration). Two different clients behind a proxy that obfuscates
        // for= are indistinguishable to it, so they share one partition and one budget --
        // the self-DoS the limiter's own comment names.
        HttpContext first = await RunThroughForwardedHeadersMiddlewareAsync("for=_YQuN68tm6");
        HttpContext second = await RunThroughForwardedHeadersMiddlewareAsync("for=_kP2xRb9La");

        first.Connection.RemoteIpAddress.Should().Be(second.Connection.RemoteIpAddress);
        first.Connection.RemoteIpAddress.Should().Be(_proxy);
    }

    [Fact]
    public async Task An_address_bearing_node_keeps_those_clients_apart()
    {
        // The refutation of the test above: with an address-bearing node -- HAProxy's
        // default -- the same two clients partition separately, so the collapse is the
        // emitting proxy's configuration and not the shim's doing.
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
