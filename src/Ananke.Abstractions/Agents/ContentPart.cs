using System.Text.Json.Serialization;

namespace Ananke.Abstractions.Agents;

/// <summary>
/// Base type for multimodal content parts within an <see cref="AgentMessage"/> or <c>AgentResponse</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(AudioPart), "audio")]
[JsonDerivedType(typeof(ImagePart), "image")]
[JsonDerivedType(typeof(ReasoningPart), "reasoning")]
[JsonDerivedType(typeof(DocumentPart), "document")]
public abstract record ContentPart;

/// <summary>A plain-text content part.</summary>
public sealed record TextPart(string Text) : ContentPart;

/// <summary>An audio content part carrying raw audio bytes.</summary>
public sealed record AudioPart(byte[] Data, string MimeType) : ContentPart
{
    /// <summary>Duration of the audio clip, when known.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Optional transcript of the audio content.</summary>
    public string? Transcript { get; init; }
}

/// <summary>An image content part carrying raw bytes or a URI reference.</summary>
public sealed record ImagePart : ContentPart
{
    /// <summary>Raw image bytes. Either <see cref="Data"/> or <see cref="Uri"/> should be set.</summary>
    public byte[]? Data { get; init; }

    /// <summary>URI to the image. Either <see cref="Data"/> or <see cref="Uri"/> should be set.</summary>
    public Uri? Uri { get; init; }

    /// <summary>MIME type of the image (e.g. "image/png").</summary>
    public required string MimeType { get; init; }

    /// <summary>Descriptive alt text for accessibility.</summary>
    public string? AltText { get; init; }
}

/// <summary>
/// Model reasoning/thinking content. Deliberately not a <see cref="TextPart"/>: excluded from
/// <see cref="AgentResponse"/>'s text concatenation, since reasoning is not part of the answer.
/// </summary>
public sealed record ReasoningPart(string Text) : ContentPart
{
    /// <summary>
    /// Opaque signature the provider issued for this reasoning block. Echo it back verbatim on a
    /// later turn when the provider requires reasoning to be re-supplied for multi-turn continuation.
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// <see langword="true"/> when the provider redacted this reasoning block (e.g. flagged by its
    /// own safety systems). <see cref="Text"/> then carries an opaque payload rather than
    /// human-readable reasoning — some providers require it echoed back verbatim, not displayed.
    /// </summary>
    public bool IsRedacted { get; init; }
}

/// <summary>A document content part carrying raw bytes or a URI reference (e.g. a PDF).</summary>
public sealed record DocumentPart : ContentPart
{
    /// <summary>Raw document bytes. Either <see cref="Data"/> or <see cref="Uri"/> should be set.</summary>
    public byte[]? Data { get; init; }

    /// <summary>URI to the document. Either <see cref="Data"/> or <see cref="Uri"/> should be set.</summary>
    public Uri? Uri { get; init; }

    /// <summary>MIME type of the document (e.g. "application/pdf").</summary>
    public required string MimeType { get; init; }

    /// <summary>Descriptive name of the document, when known.</summary>
    public string? Name { get; init; }
}
