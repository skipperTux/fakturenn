using System.Net;
using AwesomeAssertions;
using Fakturenn.Web.UnitTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// Covers the three states the design fixes for forwarded-header trust: not set,
/// set and parseable, set but nothing parses. They are deliberately not the same,
/// and the eager parse exists so the third one fails at startup rather than at some
/// request months later.
/// </summary>
public sealed class ForwardedHeaderTrustTests
{
    [Fact]
    public void Unset_trust_warns_and_leaves_X_Forwarded_headers_ignored()
    {
        RecordingLogger logger = new();

        ForwardedHeadersOptions options = Resolve(logger);

        options.ForwardedHeaders.Should().Be(
            ForwardedHeaders.None,
            "no proxy in front is a legitimate decision, and the safe direction is to trust only "
                + "what the application observes itself");
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("no trust configured", StringComparison.Ordinal));
    }

    [Fact]
    public void A_parseable_trust_list_replaces_the_middleware_defaults()
    {
        RecordingLogger logger = new();

        ForwardedHeadersOptions options = Resolve(
            logger,
            ("Network:KnownProxies", "203.0.113.7, 203.0.113.8"),
            ("Network:KnownNetworks", "10.0.0.0/8; 172.16.0.0/12"));

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.KnownProxies.Should().BeEquivalentTo(
            [IPAddress.Parse("203.0.113.7"), IPAddress.Parse("203.0.113.8")],
            "the operator's list replaces the loopback defaults rather than extending them");
        // .ToArray() is load-bearing, not noise. KnownIPNetworks is backed by
        // DualIPNetworkList, which implements IEnumerable<System.Net.IPNetwork>,
        // IEnumerable<Microsoft.AspNetCore.HttpOverrides.IPNetwork> and the non-generic
        // IEnumerable side by side. The assertion library reaches the non-generic one,
        // which yields the obsolete legacy type and fails the comparison on type rather
        // than on value. Materialising through the generic interface first pins the type.
        options.KnownIPNetworks.ToArray().Should().BeEquivalentTo(
            [IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("172.16.0.0/12")]);
        options.ForwardLimit.Should().Be(1, "one proxy is the default hop count");
    }

    [Fact]
    public void A_trust_list_where_nothing_parses_fails_startup()
    {
        RecordingLogger logger = new();

        Action act = () => Resolve(logger, ("Network:KnownProxies", "not-an-address, also-not-one"));

        act.Should().Throw<InvalidDataException>(
            "silently falling back to the middleware's loopback defaults would turn a typo into an "
                + "unexplained http:// redirect months later")
            .WithMessage("*no entry could be parsed*");
    }

    [Fact]
    public void An_unparseable_network_list_fails_startup_too()
    {
        RecordingLogger logger = new();

        Action act = () => Resolve(logger, ("Network:KnownNetworks", "10.0.0.0/notacidr"));

        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("203.0.113.7", null)]
    [InlineData(null, "10.0.0.0/8")]
    [InlineData("203.0.113.7", "10.0.0.0/8")]
    public void The_trust_lists_are_never_left_both_empty(string? proxies, string? networks)
    {
        // Empty KnownProxies AND empty KnownIPNetworks is the documented way to disable
        // trust validation entirely and honour X-Forwarded-* from any source (the ASP.NET
        // Core 8.0.17 / 9.0.6 breaking change). No reachable configuration may produce it.
        RecordingLogger logger = new();

        ForwardedHeadersOptions options = Resolve(
            logger,
            ("Network:KnownProxies", proxies),
            ("Network:KnownNetworks", networks));

        (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0).Should().BeFalse(
            "clearing both lists and leaving them empty honours forwarded headers from any source");
    }

    [Fact]
    public void Both_address_families_are_trusted()
    {
        RecordingLogger logger = new();

        ForwardedHeadersOptions options = Resolve(
            logger,
            ("Network:KnownProxies", "203.0.113.7, 2001:db8::1"),
            ("Network:KnownNetworks", "10.0.0.0/8, 2001:db8::/32"));

        options.KnownProxies.Should().BeEquivalentTo(
            [IPAddress.Parse("203.0.113.7"), IPAddress.Parse("2001:db8::1")]);
        options.KnownIPNetworks.ToArray().Should().BeEquivalentTo(
            [IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("2001:db8::/32")]);
    }

    [Fact]
    public void An_unparseable_entry_beside_a_valid_one_is_dropped_and_warned_about()
    {
        RecordingLogger logger = new();

        ForwardedHeadersOptions options = Resolve(
            logger,
            ("Network:KnownProxies", "203.0.113.7, nonsense"));

        options.KnownProxies.Should().BeEquivalentTo([IPAddress.Parse("203.0.113.7")]);
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("nonsense", StringComparison.Ordinal));
    }

    [Fact]
    public void The_resolved_trust_is_logged_as_values_not_counts()
    {
        // A count of one looks identical whether the operator chose that entry or
        // inherited it, so an operator diagnosing a proxy problem needs the addresses.
        RecordingLogger logger = new();

        Resolve(
            logger,
            ("Network:KnownProxies", "203.0.113.7"),
            ("Network:KnownNetworks", "10.0.0.0/8"));

        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("203.0.113.7", StringComparison.Ordinal)
            && entry.Message.Contains("10.0.0.0/8", StringComparison.Ordinal));
    }

    [Fact]
    public void ForwardLimit_is_configurable()
    {
        RecordingLogger logger = new();

        ForwardedHeadersOptions options = Resolve(
            logger,
            ("Network:KnownProxies", "203.0.113.7"),
            ("Network:ForwardLimit", "2"));

        options.ForwardLimit.Should().Be(2);
    }

    private static ForwardedHeadersOptions Resolve(ILogger logger, params (string Key, string? Value)[] settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Configuration.AddInMemoryCollection(
            settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));

        builder.AddForwardedHeaderTrust(logger);

#pragma warning disable ASP0000 // No hosted application here; the built provider is the assertion target.
        using ServiceProvider provider = builder.Services.BuildServiceProvider();
#pragma warning restore ASP0000
        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }
}
