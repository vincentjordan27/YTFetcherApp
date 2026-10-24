using Microsoft.Extensions.Logging;

namespace YtFetcher.Api.Tests.Channels;

public sealed class TestLogSink
{
    private readonly Lock gate = new();
    private readonly List<TestLogEntry> entries = [];

    public IReadOnlyList<TestLogEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return entries.ToArray();
            }
        }
    }

    public ILogger<T> CreateLogger<T>() => new TestLogger<T>(this);

    private void Add(TestLogEntry entry)
    {
        lock (gate)
        {
            entries.Add(entry);
        }
    }

    private sealed class TestLogger<T>(TestLogSink sink) : ILogger<T>
    {
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
            var properties = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var (key, value) in values)
                {
                    properties[key] = value;
                }
            }

            sink.Add(new TestLogEntry(logLevel, formatter(state, exception), properties));
        }
    }
}

public sealed record TestLogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);
