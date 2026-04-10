using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Extension methods for working with <see cref="AgentMessage"/> collections.
/// </summary>
public static class AgentMessageExtensions
{
    /// <summary>
    /// If the last assistant message has <see cref="AgentMessage.ToolCalls"/> without
    /// matching tool-result messages, inserts synthetic "cancelled" results so LLM APIs
    /// accept the conversation history.
    /// <para>
    /// This is required after interrupting a streaming workflow mid–tool-call.
    /// Without patching, providers like OpenAI reject the follow-up request because
    /// every <c>tool_calls</c> entry must have a corresponding <c>tool</c> message.
    /// </para>
    /// </summary>
    /// <param name="messages">The mutable conversation history to patch in-place.</param>
    /// <param name="cancelledText">
    /// The text content inserted for each synthetic tool result.
    /// Defaults to <c>"[interrupted — tool call cancelled]"</c>.
    /// </param>
    public static void PatchOrphanedToolCalls(
        this List<AgentMessage> messages,
        string cancelledText = "[interrupted — tool call cancelled]")
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];

            if (msg.Role == AgentRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                var answeredIds = new HashSet<string>();
                for (var j = i + 1; j < messages.Count; j++)
                {
                    if (messages[j].Role == AgentRole.Tool && messages[j].ToolCallId is { } id)
                        answeredIds.Add(id);
                }

                var insertAt = i + 1 + answeredIds.Count;
                foreach (var call in msg.ToolCalls)
                {
                    if (!answeredIds.Contains(call.Id))
                    {
                        messages.Insert(insertAt,
                            AgentMessage.ToolResult(call.Id, cancelledText));
                        insertAt++;
                    }
                }
                break;
            }

            if (msg.Role == AgentRole.User)
                break;
        }
    }
}
