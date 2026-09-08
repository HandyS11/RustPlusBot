using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Connections.Tests.Fakes;

/// <summary>
/// Captures every log record written through it. The supervisor's error paths are deliberately silent —
/// they swallow the exception so one failure cannot kill a loop — so the emitted log line is the only
/// observable proof that the path ran and was contained. Tests assert on that instead of on timing.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogRecord> _records = new();

    /// <summary>The captured records, oldest first. Safe to enumerate from any thread.</summary>
    public IReadOnlyCollection<LogRecord> Records => _records;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_records);

    /// <inheritdoc />
    public void Dispose() => GC.SuppressFinalize(this);

    /// <summary>One captured log line.</summary>
    /// <param name="Level">The level it was written at.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The exception attached to the record, if any.</param>
    internal sealed record LogRecord(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger(ConcurrentQueue<LogRecord> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            records.Enqueue(new LogRecord(logLevel, formatter(state, exception), exception));
    }
}
