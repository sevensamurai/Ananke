using Ananke.Abstractions.Channels;

namespace Ananke.Orchestration.Jobs;

/// <summary>
/// Factory methods for creating <see cref="HandoffJob{TState, TMessage, TResponse}"/> (workflow-level)
/// and <see cref="HandoffProxy{TMessage, TResponse}"/> (service-level) handoff instances.
/// </summary>
/// <example>
/// <code>
/// .Job("escalate", Handoff.To&lt;TriageState, HandoffPayload, HandoffReply&gt;(
///     "specialist-queue",
///     channel,
///     state =&gt; new HandoffPayload { Summary = state.Summary },
///     (state, response) =&gt; state with { Resolution = response.Text }))
/// </code>
/// </example>
public static class Handoff
{
    /// <summary>Default timeout for handoff operations when none is specified.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a handoff job that sends a message to an external agent and awaits a response.
    /// The returned <see cref="IJob{TState}"/> can be passed directly to
    /// <see cref="Workflow{TState}.Job(string, IJob{TState})"/>.
    /// </summary>
    /// <typeparam name="TState">Workflow state type.</typeparam>
    /// <typeparam name="TMessage">The outgoing message type.</typeparam>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="topic">The destination topic (e.g. a queue name or agent identifier).</param>
    /// <param name="channel">
    /// The handoff channel implementation — use <see cref="InMemoryHandoffChannel"/>
    /// for tests or <c>MqttHandoffChannel</c> for production.
    /// </param>
    /// <param name="createMessage">Extracts the outgoing message from the workflow state.</param>
    /// <param name="mapResult">Merges the external response back into the workflow state.</param>
    /// <param name="timeout">
    /// Maximum time to wait for a response. Defaults to <see cref="DefaultTimeout"/> (5 minutes).
    /// </param>
    public static HandoffJob<TState, TMessage, TResponse> To<TState, TMessage, TResponse>(
        string topic,
        IHandoffChannel channel,
        Func<TState, TMessage> createMessage,
        Func<TState, TResponse, TState> mapResult,
        TimeSpan? timeout = null)
        where TMessage : class
        where TResponse : class
    {
        return new HandoffJob<TState, TMessage, TResponse>(
            $"handoff:{topic}",
            topic,
            channel,
            createMessage,
            mapResult,
            timeout ?? DefaultTimeout);
    }

    /// <summary>
    /// Creates a typed handoff proxy for sending messages to an external service.
    /// Unlike <see cref="To{TState, TMessage, TResponse}"/>, the proxy is not tied to
    /// a workflow and can be used in endpoints, background services, or any
    /// application code that needs request-response handoff.
    /// </summary>
    /// <typeparam name="TMessage">The outgoing message type.</typeparam>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="topic">The destination topic (e.g. a queue name or agent identifier).</param>
    /// <param name="channel">
    /// The handoff channel implementation — use <see cref="InMemoryHandoffChannel"/>
    /// for tests or <c>MqttHandoffChannel</c> for production.
    /// </param>
    /// <param name="timeout">
    /// Maximum time to wait for a response. Defaults to <see cref="DefaultTimeout"/> (5 minutes).
    /// </param>
    public static HandoffProxy<TMessage, TResponse> Proxy<TMessage, TResponse>(
        string topic,
        IHandoffChannel channel,
        TimeSpan? timeout = null)
        where TMessage : class
        where TResponse : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(channel);

        return new HandoffProxy<TMessage, TResponse>(
            topic, channel, timeout ?? DefaultTimeout);
    }
}
