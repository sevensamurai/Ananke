using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

internal sealed class MinimalConsoleFormatter()
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "minimal";

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
            return;

        var level = logEntry.LogLevel switch
        {
            LogLevel.Trace       => "trce",
            LogLevel.Debug       => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning     => "warn",
            LogLevel.Error       => "fail",
            LogLevel.Critical    => "crit",
            _                    => "none"
        };

        textWriter.WriteLine($"{level}: {message}");

        if (logEntry.Exception is not null)
            textWriter.WriteLine(logEntry.Exception);
    }
}
