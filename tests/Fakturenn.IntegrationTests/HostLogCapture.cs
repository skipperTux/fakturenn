using Serilog.Core;
using Serilog.Events;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// An in-memory Serilog sink attached to the fixture's host, so a test can read exactly
/// what the application wrote.
/// <para>
/// Attached the way an operator would attach any other sink — by configuration, through
/// <c>Serilog:WriteTo</c> — rather than by a test-only hook in
/// <c>FakturennWebApplication</c>. A hook would prove that a hook works; this proves the
/// real Serilog pipeline carries the events, and it exercises the same
/// assembly-qualified-name resolution that <c>MessageFieldJsonFormatter</c> depends on.
/// </para>
/// <para>
/// The instance is static because Serilog's configuration reader resolves
/// <c>Type::Member</c> against a public static field. Every class in the
/// <see cref="RealHost"/> collection therefore writes into the same list, which is why
/// callers take a <see cref="Mark"/> first and read only what followed it.
/// </para>
/// </summary>
public sealed class HostLogCapture : ILogEventSink
{
    /// <summary>
    /// The <c>Serilog:WriteTo:&lt;n&gt;:Args:sink</c> value that selects this sink. The type
    /// and assembly names are resolved at runtime, so a rename here fails as a captureless
    /// test rather than as a compiler error.
    /// </summary>
    public const string ConfigurationName =
        "Fakturenn.IntegrationTests.HostLogCapture::Instance, Fakturenn.IntegrationTests";

    public static readonly HostLogCapture Instance = new();

    private readonly Lock _gate = new();

    private readonly List<LogEvent> _events = [];

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Add(logEvent);
        }
    }

    /// <summary>Where the log stands now. Pass it to <see cref="Since"/> afterwards.</summary>
    public int Mark()
    {
        lock (_gate)
        {
            return _events.Count;
        }
    }

    /// <summary>Everything written after <paramref name="mark"/>, as a snapshot.</summary>
    public IReadOnlyList<LogEvent> Since(int mark)
    {
        lock (_gate)
        {
            return [.. _events.Skip(mark)];
        }
    }
}
