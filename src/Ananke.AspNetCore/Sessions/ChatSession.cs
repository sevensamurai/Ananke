using System.Collections.Concurrent;
using Ananke.AspNetCore.Sse;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.StateMachine;
using Microsoft.Extensions.Logging;

namespace Ananke.AspNetCore.Sessions;

/// <summary>
/// Base session for state-machine-driven SSE chat workflows.
/// Provides conversation history, SSE binding, and streaming — the plumbing
/// every session needs. Subclass to add domain-specific state (e.g. a
/// <see cref="Ananke.Orchestration.Knowledge.KnowledgeBase"/>); or use directly
/// when no extra state is required.
/// </summary>
/// <typeparam name="TState">State enum type for the state machine.</typeparam>
/// <typeparam name="TAction">Action/trigger enum type for the state machine.</typeparam>
public class ChatSession<TState, TAction>
    where TState : Enum
    where TAction : Enum
{
    private Func<string, object, Task> _writeSse = (_, _) => Task.CompletedTask;
    private readonly ConcurrentDictionary<Type, object> _contexts = new();

    public ChatSession(
        StateMachine<TState, TAction> machine,
        IStreamingAgentModel model,
        ILogger logger)
    {
        Machine = machine;
        Model = model;
        Logger = logger;
    }

    /// <summary>The state machine driving this session's phases.</summary>
    public StateMachine<TState, TAction> Machine { get; }

    /// <summary>The streaming LLM model used by chat workflows.</summary>
    public IStreamingAgentModel Model { get; }

    /// <summary>Conversation message history, persisted across turns.</summary>
    public List<AgentMessage> Messages { get; } = [];

    /// <summary>Logger for this session.</summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Returns a lazily-created, session-scoped context object of type <typeparamref name="T"/>.
    /// Each distinct type gets exactly one instance, shared across all phases.
    /// Use this to carry domain state between phases without adding properties to the session subclass.
    /// </summary>
    /// <typeparam name="T">Context DTO type. Must be a reference type with a parameterless constructor.</typeparam>
    public T GetContext<T>() where T : class, new()
        => (T)_contexts.GetOrAdd(typeof(T), _ => new T());

    /// <summary>
    /// Binds SSE output to the current HTTP response. Called once per request.
    /// </summary>
    public void BindResponse(Func<string, object, Task> writeSse) => _writeSse = writeSse;

    /// <summary>Writes a named SSE event with JSON data to the client.</summary>
    public Task EmitAsync(string eventName, object data) => _writeSse(eventName, data);

    /// <summary>
    /// Consumes a <see cref="ChatSessionEvent"/> stream and emits corresponding SSE events.
    /// Shared by all phases that run a <see cref="StreamingChatWorkflow"/>.
    /// </summary>
    /// <param name="events">The stream of session events to relay.</param>
    /// <param name="ct">
    /// Stops relaying when the client disconnects. Pass <c>HttpContext.RequestAborted</c>.
    /// </param>
    public async Task StreamAsync(IAsyncEnumerable<ChatSessionEvent> events, CancellationToken ct = default)
    {
        await events.WriteSseAsync(_writeSse,
            onError: message => Logger.LogError("❌ Error: {Message}", message),
            ct: ct);
    }
}
