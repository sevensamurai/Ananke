namespace Ananke.Abstractions.Agents;

/// <summary>
/// The model's response to an <see cref="AgentRequest"/>. May contain text, multimodal parts,
/// tool call requests, and token usage metadata.
/// </summary>
public sealed record AgentResponse
{
    /// <summary>
    /// Multimodal content parts in the response. When set, <see cref="Text"/> is
    /// computed by concatenating text from any <see cref="TextPart"/> entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Adapter contract (ADR-arch-029 D1).</b> An implementation MUST populate this whenever the
    /// response carries <b>any content that is not a <see cref="TextPart"/></b> — reasoning, image,
    /// audio or document. For a response that is purely text it MAY be left <see langword="null"/>,
    /// with <see cref="Text"/> carrying the content; callers must therefore treat
    /// <see langword="null"/> as "text-only", not as "no content".
    /// </para>
    /// <para>
    /// The rule is deliberately not "always populate": wrapping every plain text reply in a
    /// <see cref="TextPart"/> would allocate for the common case while carrying nothing
    /// <see cref="Text"/> did not already.
    /// </para>
    /// <para>
    /// <b>The same rule binds both paths.</b> An adapter must not populate this on
    /// <c>GenerateAsync</c> and omit it on <c>GenerateStreamAsync</c>'s
    /// <see cref="AgentStreamChunk.CompletedResponse"/> for the same input — that asymmetry silently
    /// drops content when a caller switches to streaming, and is what ADR-arch-029 exists to fix.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ContentPart>? Parts { get; init; }

    private readonly string? _text;

    /// <summary>
    /// Text content of the response. When <see cref="Parts"/> is set, returns the
    /// concatenated text from <see cref="TextPart"/> entries; otherwise returns
    /// the value set directly.
    /// </summary>
    public string? Text
    {
        get
        {
            if (Parts is not { Count: > 0 })
                return _text;

            var joined = string.Concat(Parts.OfType<TextPart>().Select(p => p.Text));
            return joined.Length > 0 ? joined : null;
        }
        init => _text = value;
    }

    /// <summary>Tool calls the model wants to invoke. Check <see cref="RequiresAction"/>.</summary>
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; }

    /// <summary><see langword="true"/> when the model returned one or more tool calls.</summary>
    public bool RequiresAction => ToolCalls is { Count: > 0 };

    /// <summary>Token usage for this LLM call, if reported by the provider.</summary>
    public TokenUsage? Usage { get; init; }
}
