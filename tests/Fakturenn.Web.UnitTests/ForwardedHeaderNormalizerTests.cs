using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// The straightforward case is not where an RFC 7239 parser breaks. Every input here
/// is a form the RFC actually permits.
/// </summary>
public sealed class ForwardedHeaderNormalizerTests
{
    [Theory]
    // The RFC's own examples.
    [InlineData("for=\"_gazonk\"", null)]                                  // obfuscated: not an address
    [InlineData("for=_gazonk", null)]                                      // same, unquoted
    [InlineData("for=unknown", null)]                                      // literal unknown
    [InlineData("for=\"Unknown\"", null)]                                  // and it is case-insensitive
    [InlineData("for=192.0.2.60;proto=http;by=203.0.113.43", "192.0.2.60")]
    [InlineData("for=192.0.2.43, for=198.51.100.17", "192.0.2.43, 198.51.100.17")]
    // Quoting, ports, IPv6 -- CLAUDE.md requires both address families work.
    [InlineData("for=\"[2001:db8:cafe::17]:4711\"", "[2001:db8:cafe::17]")]
    [InlineData("for=\"[2001:db8:cafe::17]\"", "[2001:db8:cafe::17]")]
    [InlineData("for=\"192.0.2.1:1234\"", "192.0.2.1")]
    [InlineData("for=192.0.2.1", "192.0.2.1")]
    [InlineData("For=192.0.2.60", "192.0.2.60")]                           // parameter names are case-insensitive
    [InlineData("proto=https", null)]                                      // no for= at all
    [InlineData("garbage", null)]
    [InlineData("for=", null)]                                             // present but empty
    [InlineData("for=\"[2001:db8::1\"", null)]                             // unterminated bracket
    [InlineData("for=\"[not-an-address]:443\"", null)]                     // bracketed non-address
    [InlineData("for=example.test", null)]                                 // a name is not an address
    // A rejected node must not swallow the valid ones beside it.
    [InlineData("for=unknown, for=198.51.100.17", "198.51.100.17")]
    [InlineData("for=\"[2001:db8::1]:443\", for=192.0.2.1:80", "[2001:db8::1], 192.0.2.1")]
    public async Task Forwarded_is_translated_to_X_Forwarded_For(string header, string? expected)
    {
        HttpContext context = await RunAsync(headers => headers["Forwarded"] = header);

        if (expected is null)
        {
            context.Request.Headers.ContainsKey("X-Forwarded-For").Should().BeFalse(
                "an obfuscated, unknown or malformed node passed through as an address would produce "
                    + "an entry the built-in parser silently discards, which is indistinguishable "
                    + "from the header never arriving");
        }
        else
        {
            context.Request.Headers["X-Forwarded-For"].ToString().Should().Be(expected);
        }
    }

    [Fact]
    public async Task X_Forwarded_For_wins_and_Forwarded_is_not_merged_into_it()
    {
        // Merging two chains of different provenance constructs an address list that
        // never existed. Whichever header a trusted proxy sets, it must strip the
        // inbound copy of it -- that requirement is identical for both.
        HttpContext context = await RunAsync(headers =>
        {
            headers["Forwarded"] = "for=192.0.2.43;proto=https;host=forwarded.test";
            headers["X-Forwarded-For"] = "198.51.100.17";
        });

        context.Request.Headers["X-Forwarded-For"].ToString().Should().Be("198.51.100.17");
        context.Request.Headers.ContainsKey("X-Forwarded-Proto").Should().BeFalse(
            "the Forwarded header is ignored entirely, not partially");
        context.Request.Headers.ContainsKey("X-Forwarded-Host").Should().BeFalse();
    }

    [Fact]
    public async Task Proto_and_host_are_translated_from_the_first_element_that_carries_them()
    {
        HttpContext context = await RunAsync(headers =>
            headers["Forwarded"] = "for=192.0.2.43;proto=https;host=example.test, for=198.51.100.17;proto=http");

        context.Request.Headers["X-Forwarded-For"].ToString().Should().Be("192.0.2.43, 198.51.100.17");
        context.Request.Headers["X-Forwarded-Proto"].ToString().Should().Be(
            "https",
            "element one is the closest to the client, same ordering as X-Forwarded-For");
        context.Request.Headers["X-Forwarded-Host"].ToString().Should().Be("example.test");
    }

    [Fact]
    public async Task No_Forwarded_header_synthesises_nothing()
    {
        HttpContext context = await RunAsync(_ => { });

        context.Request.Headers.ContainsKey("X-Forwarded-For").Should().BeFalse();
        context.Request.Headers.ContainsKey("X-Forwarded-Proto").Should().BeFalse();
        context.Request.Headers.ContainsKey("X-Forwarded-Host").Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_Forwarded_headers_are_read_as_one_chain()
    {
        string[] chain = ["for=192.0.2.43", "for=198.51.100.17"];

        HttpContext context = await RunAsync(headers => headers["Forwarded"] = chain);

        context.Request.Headers["X-Forwarded-For"].ToString().Should().Be("192.0.2.43, 198.51.100.17");
    }

    [Fact]
    public async Task The_pipeline_continues_after_translation()
    {
        bool reached = false;

        await RunAsync(headers => headers["Forwarded"] = "for=192.0.2.43", () => reached = true);

        reached.Should().BeTrue("the shim translates and delegates; it never terminates the request");
    }

    private static async Task<HttpContext> RunAsync(Action<IHeaderDictionary> arrange, Action? onNext = null)
    {
        ServiceCollection services = new();
#pragma warning disable ASP0000 // No hosted application here; ApplicationBuilder needs a provider.
        using ServiceProvider provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

        ApplicationBuilder app = new(provider);
        app.UseRfc7239Forwarded();
        app.Run(_ =>
        {
            onNext?.Invoke();
            return Task.CompletedTask;
        });

        RequestDelegate pipeline = app.Build();

        DefaultHttpContext context = new();
        arrange(context.Request.Headers);
        await pipeline(context);
        return context;
    }
}
