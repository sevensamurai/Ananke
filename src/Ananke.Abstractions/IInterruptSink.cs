namespace Ananke.Abstractions;

/// <summary>
/// Delivers an interrupt signal with a typed payload to a running session.
/// <para>
/// In-process implementations use <see cref="CancellationTokenSource"/> cancellation
/// via <c>StateMachine&lt;S,T&gt;.OnInterrupt(sink)</c>.
/// Distributed implementations can publish over a message transport
/// (MQTT, Redis, etc.) so the interrupt reaches a remote workflow host.
/// </para>
/// </summary>
/// <typeparam name="T">The interrupt payload type (e.g. <c>AgentMessage</c>).</typeparam>
public interface IInterruptSink<in T>
{
    /// <summary>
    /// Delivers the interrupt payload and cancels any in-flight work
    /// associated with the current session.
    /// </summary>
    Task InterruptAsync(T payload, CancellationToken ct = default);
}
