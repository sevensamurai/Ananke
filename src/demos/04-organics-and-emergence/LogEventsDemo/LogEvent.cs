namespace LogEventsDemo;

/// <summary>
/// A single structured log event from the simulated distributed system.
/// </summary>
internal sealed record LogEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Service { get; init; }
    public required LogLevel Level { get; init; }
    public required string Message { get; init; }
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
    public string? CorrelationId { get; init; }
    public string? SpanId { get; init; }

    public override string ToString()
    {
        var levelTag = Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
        return $"{Timestamp:HH:mm:ss.fff} [{levelTag}] {Service,-20} {Message}";
    }
}

/// <summary>Log severity levels for the simulated system.</summary>
internal enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}
