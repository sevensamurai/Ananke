namespace Ananke.Abstractions.Agents;

/// <summary>Why the model stopped producing its reply.</summary>
/// <remarks>
/// Each adapter maps its provider's own value onto these. A value a provider sends that none of
/// these names becomes <see cref="Unknown"/>, never the nearest guess.
/// </remarks>
public enum AgentStopReason
{
    /// <summary>The provider did not say, or said something this enum does not name.</summary>
    Unknown,

    /// <summary>The model finished its reply.</summary>
    EndTurn,

    /// <summary>The model stopped to call one or more tools.</summary>
    ToolCalls,

    /// <summary>The reply reached the output-token limit and is cut off.</summary>
    MaxTokens,

    /// <summary>The provider's content filter stopped the reply.</summary>
    ContentFilter,

    /// <summary>The model declined to answer.</summary>
    Refusal
}
