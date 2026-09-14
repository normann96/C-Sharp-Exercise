using Microsoft.Extensions.Logging;

namespace CSharpApp.IntegrationTests.TestDoubles;

public sealed record CapturedLogEvent(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties, Exception? Exception);

/// <summary>Sits next to Serilog in the host's logging fan-out and keeps every event with its structured properties.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly Lock _lock = new();
    private readonly List<CapturedLogEvent> _events = [];

    public IReadOnlyList<CapturedLogEvent> Events
    {
        get { lock (_lock) { return [.. _events]; } }
    }

    public int Count
    {
        get { lock (_lock) { return _events.Count; } }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(CapturedLogEvent captured)
    {
        lock (_lock)
        {
            _events.Add(captured);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IReadOnlyList<KeyValuePair<string, object?>> pairs
                ? pairs.Where(pair => pair.Key != OriginalFormatKey).ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            provider.Add(new CapturedLogEvent(category, logLevel, formatter(state, exception), properties, exception));
        }
    }
}
