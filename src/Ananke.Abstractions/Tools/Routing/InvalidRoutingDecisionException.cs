namespace Ananke.Abstractions.Tools.Routing;

/// <summary>
/// Thrown when a stage in a <c>CompositeSmartToolRouter</c> violates the
/// subset invariant (returns tools the previous stage did not include).
/// </summary>
public sealed class InvalidRoutingDecisionException : InvalidOperationException
{
    /// <inheritdoc cref="InvalidOperationException(string)"/>
    public InvalidRoutingDecisionException(string message) : base(message) { }
}
