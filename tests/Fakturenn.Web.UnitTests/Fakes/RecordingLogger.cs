using Microsoft.Extensions.Logging;

namespace Fakturenn.Web.UnitTests.Fakes;

internal sealed record LogEntry(LogLevel Level, string Message);

/// <summary>
/// Captures what was logged. Startup trust configuration reports two of its three
/// states through the log rather than through a return value, so the log is part of
/// the behaviour under test, not incidental output.
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
