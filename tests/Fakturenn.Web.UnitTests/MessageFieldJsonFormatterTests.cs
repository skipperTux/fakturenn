using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;

namespace Fakturenn.Web.UnitTests;

/// <summary>
/// <c>MessageFieldJsonFormatter</c>, reached the only way a deployment ever reaches it: by
/// the assembly-qualified name in Serilog configuration.
/// <para>
/// Nothing in <c>Fakturenn.Web</c> names the type in C#, so a typo in that string — or a
/// rename of the type, or the loss of the project reference that puts the assembly next to
/// the host — fails at <b>runtime</b>, in the one deployment that selected the formatter,
/// and never at compile time. Constructing the formatter directly here would test the
/// half that cannot break.
/// </para>
/// <para>
/// This test project references only <c>Fakturenn.Web</c>. The formatter's assembly is
/// therefore present in the output solely because <c>Fakturenn.Web.csproj</c> references it,
/// which is what makes deleting that reference fail here instead of in production.
/// </para>
/// </summary>
public sealed class MessageFieldJsonFormatterTests : IDisposable
{
    /// <summary>
    /// The exact string <c>DEPLOYMENT-BASELINE.md</c> tells an operator to paste. It is the
    /// subject of this test, not a detail of it.
    /// </summary>
    private const string FormatterName =
        "Fakturenn.Infrastructure.Logging.MessageFieldJsonFormatter, Fakturenn.Infrastructure.Logging";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"fakturenn-formatter-{Guid.CreateVersion7()}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void The_formatter_resolves_through_configuration_and_writes_one_json_object_per_event()
    {
        JsonElement written = WriteOneEvent();

        // The rendered message, not the template. A formatter that emitted
        // "AuthEvent {Event} {Email}" looks correct in review and is useless in a log store.
        string message = written.GetProperty("_msg").GetString()!;
        message.Should().Contain("AdminLockedUser").And.Contain("victim@example.test");
        message.Should().NotContain("{Event}").And.NotContain("{Email}");

        // Structured properties survive as their own fields, so a query can select on the
        // event name without parsing the sentence.
        written.GetProperty("Event").GetString().Should().Be("AdminLockedUser");
        written.GetProperty("Email").GetString().Should().Be("victim@example.test");

        written.GetProperty("level").GetString().Should().Be("Warning");
        DateTimeOffset.TryParse(
            written.GetProperty("_time").GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out _).Should().BeTrue("_time must be a round-trippable timestamp");
    }

    /// <summary>
    /// Logs one event through a Serilog pipeline built from configuration that names the
    /// formatter, and returns the parsed line.
    /// </summary>
    private JsonElement WriteOneEvent()
    {
        string path = Path.Combine(_directory, "log.json");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:Using:0"] = "Serilog.Sinks.File",
                ["Serilog:MinimumLevel"] = "Information",
                ["Serilog:WriteTo:0:Name"] = "File",
                ["Serilog:WriteTo:0:Args:path"] = path,
                ["Serilog:WriteTo:0:Args:formatter"] = FormatterName,
            })
            .Build();

        using (Logger logger = new LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger())
        {
            logger.Warning("AuthEvent {Event} {Email}", "AdminLockedUser", "victim@example.test");
        }

        string[] lines = File.ReadAllLines(path);

        // One object per event. Measured: dropping one "t" from the type name above makes
        // ReadFrom.Configuration itself throw
        // ("Type Fakturenn.Infrastructure.Logging.MessageFieldJsonFormater was not found"),
        // so the typo this test exists to catch never reaches these assertions at all --
        // which is the loudest failure available and better than a silent fallback.
        lines.Should().ContainSingle("the formatter writes exactly one line per event");

        return JsonDocument.Parse(lines[0]).RootElement.Clone();
    }
}
