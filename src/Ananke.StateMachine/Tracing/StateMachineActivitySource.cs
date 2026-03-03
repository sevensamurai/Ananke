using System.Diagnostics;

namespace Ananke.StateMachine.Tracing;

/// <summary>
/// Shared <see cref="ActivitySource"/> for the state machine engine.
/// Spans are only created when a listener is attached (e.g. OpenTelemetry SDK, Aspire Dashboard).
/// This is zero-cost when no listener is registered.
/// </summary>
public static class StateMachineActivitySource
{
    public const string Name = "Ananke.StateMachine";

    public static ActivitySource Source { get; } = new(Name);
}
