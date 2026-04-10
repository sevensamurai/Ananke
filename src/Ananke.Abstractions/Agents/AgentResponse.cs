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
