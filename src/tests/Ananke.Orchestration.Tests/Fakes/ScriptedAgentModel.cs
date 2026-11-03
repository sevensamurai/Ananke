using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Tests.Fakes;

/// <summary>Answers with the given responses in order, repeating the last; records every request.</summary>
internal sealed class ScriptedAgentModel(params AgentResponse[] responses) : IStreamingAgentModel
{
    private int _next;
    public List<AgentRequest> Requests { get; } = [];

    public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(responses[Math.Min(_next++, responses.Length - 1)]);
    }

    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await GenerateAsync(request, ct);
        if (response.Text is not null) yield return new AgentStreamChunk { TextDelta = response.Text };
        yield return new AgentStreamChunk { CompletedResponse = response };
    }

    public static AgentResponse Call(string id, string tool, string args) =>
        new() { ToolCalls = [new AgentToolCall(id, tool, args)] };

    public static AgentResponse Say(string text) => new() { Text = text };
}
