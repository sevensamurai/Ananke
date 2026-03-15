using Ananke.AspNetCore.Sessions;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Memory;

/// <summary>
/// Creates fully-wired <see cref="AdoptionSession"/> instances.
/// Built once at startup with singleton dependencies (model, knowledge store);
/// called by <see cref="InMemorySessionStore{T}.GetOrCreate"/> on the first request for a session ID.
/// </summary>
internal sealed class SessionFactory(
    IStreamingAgentModel model,
    KnowledgeBase knowledge,
    IConversationMemory memory,
    ILogger logger)
{
    internal async Task<AdoptionSession> CreateAsync(string sessionId, IReadOnlyList<HistoryMessage>? history = null)
    {
        var machine = AdoptionMachine.Create();
        var session = new AdoptionSession(machine, model, knowledge, logger);

        // Restore prior conversation from distributed memory (Redis or in-memory)
        var prior = await memory.GetHistoryAsync(sessionId);
        if (prior.Count > 0)
        {
            foreach (var msg in prior)
                session.Messages.Add(msg);
        }
        else if (history is not null)
        {
            // Fall back to client-provided history (first request only)
            foreach (var msg in history)
                session.Messages.Add(msg.Role == "user"
                    ? AgentMessage.User(msg.Content)
                    : AgentMessage.Assistant(msg.Content));
        }

        SearchPhase.Register(session);
        InterruptPhase.Register(session);
        PaperworkPhase.Register(session);
        PaymentPhase.Register(session);

        return session;
    }

    /// <summary>
    /// Persists the current conversation messages to distributed memory.
    /// Called after each chat turn completes.
    /// </summary>
    internal async Task SaveConversationAsync(string sessionId, IReadOnlyList<AgentMessage> messages)
    {
        await memory.ClearAsync(sessionId);
        await memory.AddAsync(sessionId, messages);
    }

    /// <summary>Clears conversation memory for a completed session.</summary>
    internal Task ClearConversationAsync(string sessionId) => memory.ClearAsync(sessionId);
}
