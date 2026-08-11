using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Fakturenn.Web;

/// <summary>
/// Translates an RFC 7239 <c>Forwarded</c> header into the <c>X-Forwarded-*</c>
/// headers ASP.NET Core understands, so the standardised header works without
/// reimplementing trust evaluation.
/// <para>
/// This grants nothing. The built-in middleware still requires the connection's peer
/// address to match a configured proxy or network before it honours any forwarded
/// header, and that check runs after this translation.
/// </para>
/// </summary>
public static class ForwardedHeaderNormalizer
{
    public static void UseRfc7239Forwarded(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            // X-Forwarded-For wins when both are present. Whichever header the trusted
            // proxy sets, it must strip the inbound copy -- that requirement is
            // identical for both -- so precedence is about not changing behaviour for
            // the far more widely deployed header, not about safety.
            if (!context.Request.Headers.TryGetValue("Forwarded", out var forwarded)
                || context.Request.Headers.ContainsKey("X-Forwarded-For"))
            {
                await next();
                return;
            }

            List<string> fors = [];
            string? proto = null;
            string? host = null;

            // A Forwarded header is a comma-separated chain of elements, each a
            // semicolon-separated list of parameters. Order matters: element one is
            // the closest to the client, same as X-Forwarded-For.
            foreach (string element in string.Join(',', forwarded.ToArray()).Split(','))
            {
                foreach (string parameter in element.Split(';'))
                {
                    int equals = parameter.IndexOf('=', StringComparison.Ordinal);
                    if (equals < 0)
                    {
                        continue;
                    }

                    string name = parameter[..equals].Trim().ToLowerInvariant();
                    string value = Unquote(parameter[(equals + 1)..].Trim());

                    switch (name)
                    {
                        case "for" when TryReadNode(value, out string? node):
                            fors.Add(node);
                            break;
                        case "proto":
                            proto ??= value;
                            break;
                        case "host":
                            host ??= value;
                            break;
                        default:
                            break;
                    }
                }
            }

            if (fors.Count > 0)
            {
                context.Request.Headers["X-Forwarded-For"] = string.Join(", ", fors);
            }

            if (proto is not null)
            {
                context.Request.Headers["X-Forwarded-Proto"] = proto;
            }

            if (host is not null)
            {
                context.Request.Headers["X-Forwarded-Host"] = host;
            }

            await next();
        });
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    /// <summary>
    /// Extracts an address from an RFC 7239 node identifier, rejecting the ones that
    /// are not addresses at all.
    /// </summary>
    private static bool TryReadNode(string value, [NotNullWhen(true)] out string? address)
    {
        address = null;

        // RFC 7239 section 6.3 permits obfuscated identifiers such as "_hidden", and
        // section 6.2 permits the literal "unknown". Neither is an address; passing
        // either through as one would produce a garbage X-Forwarded-For entry that the
        // built-in parser then silently discards, which looks identical to the header
        // being absent.
        if (value.Length == 0 || value[0] == '_' || value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // IPv6 is bracketed and may carry a port: [2001:db8::1]:8080
        if (value[0] == '[')
        {
            int close = value.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            string inner = value[1..close];
            if (!IPAddress.TryParse(inner, out _))
            {
                return false;
            }

            address = value[..(close + 1)];
            return true;
        }

        // IPv4 may carry a port: 192.0.2.1:1234
        int colon = value.IndexOf(':', StringComparison.Ordinal);
        string candidate = colon >= 0 ? value[..colon] : value;

        if (!IPAddress.TryParse(candidate, out _))
        {
            return false;
        }

        address = candidate;
        return true;
    }
}
