namespace Ananke.Orchestration.Routing;

/// <summary>
/// Thrown by <see cref="AgentRouter{TState}"/> when the LLM response cannot be
/// matched to any declared routing option after all retry attempts are exhausted.
/// </summary>
public sealed class AgentRoutingException : InvalidOperationException
{
    /// <summary>The raw value returned by the model that could not be matched.</summary>
    public string UnexpectedValue { get; }

    /// <summary>The set of valid routing options the model was asked to choose from.</summary>
    public IReadOnlyList<string> AvailableOptions { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AgentRoutingException"/>.
    /// </summary>
    /// <param name="unexpectedValue">The unrecognized value returned by the model.</param>
    /// <param name="availableOptions">The declared routing options.</param>
    public AgentRoutingException(string unexpectedValue, IReadOnlyList<string> availableOptions)
        : base(
            $"Agent router returned '{unexpectedValue}', which does not match any available option: " +
            string.Join(", ", availableOptions) + ".")
    {
        UnexpectedValue = unexpectedValue;
        AvailableOptions = availableOptions;
    }
}
