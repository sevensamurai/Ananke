using System.Text.Json.Serialization;

namespace Ananke.Abstractions.Agents;

/// <summary>
/// Base type for multimodal content parts within an <see cref="AgentMessage"/> or <c>AgentResponse</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(AudioPart), "audio")]
[JsonDerivedType(typeof(ImagePart), "image")]
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
