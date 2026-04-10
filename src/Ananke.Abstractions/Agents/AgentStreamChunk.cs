namespace Ananke.Abstractions.Agents;

/// <summary>
/// A single chunk emitted during a streaming LLM completion.
/// Consumers receive incremental <see cref="TextDelta"/> values as the model generates text,
/// then a final chunk with <see cref="CompletedResponse"/> containing the fully assembled result.
/// </summary>
public sealed record AgentStreamChunk
{
    /// <summary>Incremental text content. Append to previous chunks to build the full response.</summary>
    public string? TextDelta { get; init; }

    /// <summary>Incremental audio bytes for audio-output models.</summary>
    public byte[]? AudioDelta { get; init; }

    /// <summary>MIME type of <see cref="AudioDelta"/> (e.g. "audio/pcm").</summary>
    public string? AudioMimeType { get; init; }

    /// <summary>Incremental transcript text corresponding to <see cref="AudioDelta"/>.</summary>
    public string? TranscriptDelta { get; init; }

    /// <summary>
    /// The fully assembled <see cref="AgentResponse"/>, populated only on the final chunk.
    /// When non-null, the stream is complete and no further chunks will be emitted.
    /// </summary>
    public AgentResponse? CompletedResponse { get; init; }
}
