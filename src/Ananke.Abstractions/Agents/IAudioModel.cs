namespace Ananke.Abstractions.Agents;

/// <summary>
/// Abstraction over an audio model provider, covering speech-to-text transcription
/// and text-to-speech synthesis. Sibling to <see cref="IAgentModel"/> and
/// <see cref="IEmbeddingModel"/>.
/// </summary>
/// <remarks>
/// Register <see cref="NullAudioModel"/> as the default when no live provider is
/// configured. Provider implementations ship in the individual
/// <c>Ananke.Orchestration.*</c> packages.
/// </remarks>
public interface IAudioModel
{
    /// <summary>
    /// Transcribes the audio in <paramref name="audio"/> and returns the text content.
    /// </summary>
    /// <param name="audio">Audio content to transcribe. Must carry a supported MIME type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transcribed text, or <see cref="string.Empty"/> if the audio is silent or empty.</returns>
    Task<string> TranscribeAsync(AudioPart audio, CancellationToken ct = default);

    /// <summary>
    /// Synthesizes <paramref name="text"/> into audio and returns the result.
    /// </summary>
    /// <param name="text">The text to synthesize.</param>
    /// <param name="options">Optional synthesis parameters. Pass <see langword="null"/> to use provider defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="AudioPart"/> containing the synthesized audio bytes and MIME type.</returns>
    Task<AudioPart> SynthesizeAsync(string text, AudioOptions? options = null, CancellationToken ct = default);
}

/// <summary>
/// No-op implementation of <see cref="IAudioModel"/>.
/// <see cref="TranscribeAsync"/> returns <see cref="string.Empty"/>;
/// <see cref="SynthesizeAsync"/> returns an <see cref="AudioPart"/> with an empty
/// byte array and MIME type <c>audio/wav</c>.
/// </summary>
/// <remarks>
/// Use as the default DI registration in test and development environments where
/// no audio provider is available.
/// </remarks>
public sealed class NullAudioModel : IAudioModel
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullAudioModel Instance = new();

    private NullAudioModel() { }

    /// <inheritdoc />
    public Task<string> TranscribeAsync(AudioPart audio, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    /// <inheritdoc />
    public Task<AudioPart> SynthesizeAsync(string text, AudioOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new AudioPart([], "audio/wav"));
}
