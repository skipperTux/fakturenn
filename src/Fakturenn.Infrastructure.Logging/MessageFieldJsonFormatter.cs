using System.Globalization;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace Fakturenn.Infrastructure.Logging;

/// <summary>
/// One JSON object per event, with the rendered message under <c>_msg</c>.
/// <para>
/// Some log stores take a line's headline text from a field of exactly that name and
/// render a placeholder when it is absent, leaving the real text one click away in
/// every row. That cannot be fixed outside the application, so the formatter ships
/// here — but it is NOT selected by default. The human-readable console formatter
/// stays the default and an operator selects this one through Serilog configuration.
/// </para>
/// <para>
/// The type and assembly name are part of the contract: configuration names them, as
/// <c>"Fakturenn.Infrastructure.Logging.MessageFieldJsonFormatter, Fakturenn.Infrastructure.Logging"</c>.
/// Renaming either — or dropping <c>Fakturenn.Web</c>'s project reference, which is what
/// puts this assembly next to the host — is a breaking change for a deployment that has
/// adopted it, and it fails at <b>runtime</b> rather than at compile time. That is why
/// <c>MessageFieldJsonFormatterTests</c> resolves it through the same configuration
/// string instead of calling the constructor.
/// </para>
/// </summary>
public sealed class MessageFieldJsonFormatter : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        output.Write("{\"_time\":\"");
        output.Write(logEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        output.Write("\",\"level\":\"");
        output.Write(logEvent.Level);
        output.Write("\",\"_msg\":");
        WriteJsonString(logEvent.RenderMessage(CultureInfo.InvariantCulture), output);

        foreach ((string name, LogEventPropertyValue value) in logEvent.Properties)
        {
            output.Write(',');
            WriteJsonString(name, output);
            output.Write(':');

            // Trimmed rather than rendered through the property's own JSON: a scalar
            // string property renders as "value" with the quotes included, and writing
            // that through WriteJsonString again would produce "\"value\"" in the output.
            WriteJsonString(value.ToString(null, CultureInfo.InvariantCulture).Trim('"'), output);
        }

        if (logEvent.Exception is not null)
        {
            output.Write(",\"exception\":");
            WriteJsonString(logEvent.Exception.ToString(), output);
        }

        output.WriteLine('}');
    }

    private static void WriteJsonString(string value, TextWriter output) =>
        output.Write(JsonSerializer.Serialize(value));
}
