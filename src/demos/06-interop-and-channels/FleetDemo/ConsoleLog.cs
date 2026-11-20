using Microsoft.Extensions.Logging;

namespace FleetDemo;

/// <summary>
/// One line per log entry, starting with the process's name. Shows the MQTT clients' connects, disconnects and
/// reconnect attempts; the state machine's own refusals are left to the node's summary.
/// </summary>
public sealed class ConsoleLog<T>(string process) : ILogger<T>
{
    private static readonly string Category = typeof(T).FullName ?? typeof(T).Name;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= Minimum && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception is not null && !message.Contains(exception.Message, StringComparison.Ordinal))
            message += $" ({exception.Message})";
        Console.WriteLine($"{process}  {Label(logLevel)}{message}");
    }

    private static LogLevel Minimum { get; } =
        Category.StartsWith("Ananke.MQTT", StringComparison.Ordinal) ? LogLevel.Information
        : Category.StartsWith("Ananke.StateMachine", StringComparison.Ordinal) ? LogLevel.Error
        : LogLevel.Warning;

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Warning => "warning: ",
        LogLevel.Error or LogLevel.Critical => "error: ",
        _ => "",
    };
}
