using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// Microsoft.AspNetCore.HttpOverrides also declares an IPNetwork, obsoleted in favour of
// System.Net.IPNetwork ("Please use System.Net.IPNetwork instead", aka.ms/aspnet/deprecate/005).
// Both namespaces are needed here -- ForwardedHeadersOptions from one, IPAddress from the
// other -- so the name is ambiguous without this alias. ForwardedHeadersOptions.KnownIPNetworks
// is typed IList<System.Net.IPNetwork>; the obsolete KnownNetworks is the legacy-typed view of
// the same backing list.
using IPNetwork = System.Net.IPNetwork;

namespace Fakturenn.Web;

/// <summary>
/// Configures which proxies may set <c>X-Forwarded-*</c>.
/// <para>
/// Trust is expressed as delimiter-separated strings rather than configuration
/// arrays, because .NET binds arrays by index: an environment variable can overwrite
/// individual elements of a list from appsettings.json but cannot replace the list.
/// An operator who wants exactly two trusted proxies and nothing inherited cannot say
/// so with an array. One string in one variable can be replaced wholesale.
/// </para>
/// </summary>
public static partial class ForwardedHeaderTrust
{
    private static readonly char[] _separators = [',', ';'];

    public static void AddForwardedHeaderTrust(this WebApplicationBuilder builder, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string? proxyList = builder.Configuration["Network:KnownProxies"];
        string? networkList = builder.Configuration["Network:KnownNetworks"];
        int forwardLimit = builder.Configuration.GetValue("Network:ForwardLimit", 1);

        bool configured = !string.IsNullOrWhiteSpace(proxyList) || !string.IsNullOrWhiteSpace(networkList);

        // ASPNETCORE_FORWARDEDHEADERS_ENABLED clears both trust lists and enables
        // XForwardedFor|XForwardedProto, which honours forwarded headers from ANY
        // source. It is widely suggested for cloud environments where proxy addresses
        // rotate, so an operator may set it without realising it overrides everything
        // configured here. Warn loudly rather than let it pass silently.
        if (string.Equals(
                Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            LogEnvironmentOverride(logger);
        }

        // Parse eagerly rather than inside the Configure callback: a trust list that
        // binds but whose entries are all unparseable must fail at startup, not have
        // the middleware silently fall back to loopback and drop every forwarded
        // header at request time. A typo would otherwise surface months later as an
        // unexplained http:// redirect.
        List<IPAddress> proxies = Parse<IPAddress>(proxyList, IPAddress.TryParse, "KnownProxy", logger);
        List<IPNetwork> networks = Parse<IPNetwork>(networkList, IPNetwork.TryParse, "KnownNetwork", logger);

        if (proxies.Count == 0 && networks.Count == 0)
        {
            if (configured)
            {
                throw new InvalidDataException(
                    "Network:KnownProxies/KnownNetworks were set but no entry could be parsed.");
            }

            // Not set is a decision, not an error: no reverse proxy in front. X-Forwarded-*
            // stay ignored, which is the safe direction -- the application trusts only what
            // it observes itself.
            LogNoTrustConfigured(logger);
            return;
        }

        // Clearing BOTH lists is documented as the way to disable trust validation
        // entirely and honour forwarded headers from any source -- see the ASP.NET
        // Core 8.0.17 / 9.0.6 breaking change "Forwarded headers middleware ignores
        // X-Forwarded-* headers from unknown proxies". This code must therefore never
        // clear and leave empty. The guard above returns before reaching here when
        // nothing parsed, so by this point at least one entry exists; assert it rather
        // than rely on a reader tracing the control flow.
        if (proxies.Count == 0 && networks.Count == 0)
        {
            throw new InvalidOperationException(
                "Refusing to clear the forwarded-header trust lists with nothing to replace them: "
                + "empty lists disable trust validation and honour headers from any source.");
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = forwardLimit;

            // Replace, do not extend: the middleware ships loopback defaults, and an
            // explicit trust list should be exactly what the operator asked for.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            proxies.ForEach(options.KnownProxies.Add);
            networks.ForEach(options.KnownIPNetworks.Add);
        });

        // Log resolved VALUES, not counts. A count of one looks identical whether the
        // operator chose that entry or inherited it. The two joins are materialised
        // into locals rather than inlined as log arguments: CA1873 rejects an argument
        // it considers expensive, and neither the generated log method's own IsEnabled
        // check nor an explicit one around the call satisfies it, because the caller
        // evaluates arguments first either way. Joining unconditionally costs nothing
        // real -- this runs once, at startup, over a handful of entries.
        string resolvedProxies = string.Join(", ", proxies);
        string resolvedNetworks = string.Join(", ", networks);

        LogTrustResolved(logger, resolvedProxies, resolvedNetworks, forwardLimit);
    }

    private delegate bool TryParse<T>(string value, out T? result);

    private static List<T> Parse<T>(string? value, TryParse<T> tryParse, string label, ILogger logger)
    {
        List<T> parsed = [];

        if (string.IsNullOrWhiteSpace(value))
        {
            return parsed;
        }

        foreach (string token in value.Split(
                     _separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (tryParse(token, out T? result) && result is not null)
            {
                parsed.Add(result);
            }
            else
            {
                LogInvalidEntry(logger, label, token);
            }
        }

        return parsed;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "ASPNETCORE_FORWARDEDHEADERS_ENABLED is set. It clears the forwarded-header trust "
            + "lists and accepts X-Forwarded-* from any source, overriding Network:KnownProxies "
            + "and Network:KnownNetworks. Unset it and configure trust explicitly.")]
    private static partial void LogEnvironmentOverride(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "ForwardedHeaders: no trust configured, X-Forwarded-* headers are ignored")]
    private static partial void LogNoTrustConfigured(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "ForwardedHeaders: trusting proxies [{Proxies}], networks [{Networks}], ForwardLimit {ForwardLimit}")]
    private static partial void LogTrustResolved(ILogger logger, string proxies, string networks, int forwardLimit);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ignoring invalid ForwardedHeaders {Label} {Value}")]
    private static partial void LogInvalidEntry(ILogger logger, string label, string value);
}
