namespace Ananke.Abstractions.Agents;

/// <summary>Describes a tool the model can invoke during generation.</summary>
public sealed record AgentTool(string Name, string Description, string ParametersJsonSchema);

/// <summary>Constrains model output to a specific JSON schema.</summary>
public sealed record AgentResponseFormat(string SchemaName, string JsonSchema, bool Strict = true);

/// <summary>
/// A request to an <see cref="IAgentModel"/>. Contains the conversation messages,
/// optional tools, optional structured output format, and provider metadata.
/// </summary>
public sealed record AgentRequest
{
    /// <summary>System prompt prepended to the conversation.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The conversation messages to send to the model.</summary>
    public required IReadOnlyList<AgentMessage> Messages { get; init; }

    /// <summary>Tools the model may invoke. When <see langword="null"/>, no tool calling is enabled.</summary>
    public IReadOnlyList<AgentTool>? Tools { get; init; }

    /// <summary>When set, constrains the model to output JSON matching the specified schema.</summary>
    public AgentResponseFormat? ResponseFormat { get; init; }

    /// <summary>Arbitrary key-value metadata forwarded to the provider.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// When <c>true</c>, the provider stores the completion so it appears in platform logs
    /// (e.g. <see href="https://platform.openai.com/logs"/>). Default is <c>false</c>.
    /// </summary>
    public bool StoreCompletions { get; init; }

    /// <summary>
    /// Sampling temperature. <see langword="null"/> — the default — sends nothing, so the provider
    /// applies its own default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nullable because <c>0</c> is a meaningful value.</b> Deterministic sampling and "unset"
    /// must stay distinguishable, or every request silently pins a temperature the caller never
    /// chose.
    /// </para>
    /// <para>
    /// <b>The default differs by job kind, deliberately.</b> Workflow jobs — <c>AgentJob</c> and
    /// <c>TextAgentJob</c> — default to <c>0</c>, because a workflow that reruns should reach the
    /// same answer. Interactive sessions — <c>StreamingChatWorkflow</c> — leave this
    /// <see langword="null"/>, because a chat that always answers identically is a worse chat.
    /// Either can be overridden with <c>WithTemperature</c>.
    /// </para>
    /// <para>
    /// <b>Passed through, not validated.</b> Providers disagree on the accepted range — roughly
    /// 0–2 for OpenAI and Gemini, 0–1 for Anthropic — so a value outside a provider's range is
    /// rejected by that provider rather than clamped here. Clamping would silently change the
    /// caller's request, which is the failure mode this type avoids elsewhere.
    /// </para>
    /// <para>
    /// <b>Some models accept no value at all.</b> Anthropic removed sampling parameters on Opus 4.7
    /// and later, rejecting them by <i>presence</i> rather than by value — so any temperature is a
    /// 400 there, including <c>1.0</c>. Leaving this <see langword="null"/> is the correct wire
    /// shape for those models, and is the default for exactly that reason.
    /// </para>
    /// </remarks>
    public double? Temperature { get; init; }
}
