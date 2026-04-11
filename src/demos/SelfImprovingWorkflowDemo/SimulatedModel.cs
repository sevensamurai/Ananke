using Ananke.Abstractions.Agents;
using System.Runtime.CompilerServices;

namespace SelfImprovingWorkflowDemo;

/// <summary>
/// A simulated LLM that returns scripted responses based on request context.
/// Allows the demo to run without real API keys.
/// </summary>
internal sealed class SimulatedModel(Func<AgentRequest, string> responder) : IStreamingAgentModel
{
    public static SimulatedModel Fixed(string responseText) =>
        new(_ => responseText);

    public static SimulatedModel Dynamic(Func<AgentRequest, string> responder) =>
        new(responder);

    public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var text = responder(request);
        return Task.FromResult(new AgentResponse
        {
            Text = text,
            Usage = new TokenUsage { InputTokens = 100, OutputTokens = 50 }
        });
    }

    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var text = responder(request);
        await Task.Yield();
        yield return new AgentStreamChunk
        {
            TextDelta = text,
            CompletedResponse = new AgentResponse
            {
                Text = text,
                Usage = new TokenUsage { InputTokens = 100, OutputTokens = 50 }
            }
        };
    }
}
