namespace Ananke.Abstractions.Agents;

/// <summary>
/// Optional parameters for audio synthesis via <see cref="IAudioModel.SynthesizeAsync"/>.
/// All properties are optional; omit the instance entirely to use provider defaults.
/// </summary>
public sealed record AudioOptions
{
    /// <summary>
    /// Provider-specific voice identifier (e.g. <c>"alloy"</c>, <c>"en-US-JennyNeural"</c>).
    /// When <see langword="null"/>, the provider default voice is used.
    /// </summary>
    public string? Voice { get; init; }

    /// <summary>
    /// Playback speed multiplier. <c>1.0</c> is normal speed; values above 1.0 speed up,
    /// below 1.0 slow down. When <see langword="null"/>, the provider default is used.
    /// </summary>
    public float? SpeedFactor { get; init; }

    /// <summary>
    /// Desired output MIME type (e.g. <c>"audio/wav"</c>, <c>"audio/ogg"</c>,
    /// <c>"audio/mpeg"</c>). When <see langword="null"/>, the provider default is used.
    /// </summary>
    public string? Format { get; init; }
}
