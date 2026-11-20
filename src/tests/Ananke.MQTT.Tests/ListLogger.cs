using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Ananke.MQTT.Tests;

/// <summary>Keeps every formatted log message, for counting what a client logged.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public int Count(string fragment) => Messages.Count(m => m.Contains(fragment, StringComparison.Ordinal));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
}
