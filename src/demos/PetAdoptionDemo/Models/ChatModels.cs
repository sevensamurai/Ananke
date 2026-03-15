internal sealed record ChatRequest
{
    public string? Message { get; init; }
    public string? AudioBase64 { get; init; }
    public string? AudioMimeType { get; init; }
    public string? ImageBase64 { get; init; }
    public string? ImageMimeType { get; init; }
    public string? SessionId { get; init; }
    public List<HistoryMessage>? History { get; init; }
}

internal sealed record HistoryMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

internal sealed record InterruptRequest
{
    public required string SessionId { get; init; }
    public string? Message { get; init; }
}
