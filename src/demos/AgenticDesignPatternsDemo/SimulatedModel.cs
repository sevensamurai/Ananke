using System.Runtime.CompilerServices;
using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;

namespace AgenticDesignPatternsDemo;

/// <summary>
/// A simulated LLM that returns scripted JSON responses based on the request context.
/// Allows the demo to run without real API keys while showcasing every pattern.
/// </summary>
internal sealed class SimulatedModel(Func<AgentRequest, string> responder, int inputTokens = 100, int outputTokens = 50) : IStreamingAgentModel
{
    private readonly Func<AgentRequest, string> _responder = responder;
    private readonly int _inputTokens = inputTokens;
    private readonly int _outputTokens = outputTokens;

    /// <summary>Creates a model that always returns the same JSON text.</summary>
    public static SimulatedModel Fixed(string responseText, int inputTokens = 100, int outputTokens = 50) =>
        new(_ => responseText, inputTokens, outputTokens);

    /// <summary>Creates a model that serializes a fixed object as JSON.</summary>
    public static SimulatedModel Json<T>(T response, int inputTokens = 100, int outputTokens = 50) =>
        new(_ => JsonSerializer.Serialize(response), inputTokens, outputTokens);

    public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var text = _responder(request);
        return Task.FromResult(new AgentResponse
        {
            Text = text,
            Usage = new TokenUsage { InputTokens = _inputTokens, OutputTokens = _outputTokens }
        });
    }

    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var text = _responder(request);
        await Task.Yield();

        // Stream word by word for realistic streaming demo
        var words = text.Split(' ');
        foreach (var word in words)
        {
            yield return new AgentStreamChunk { TextDelta = word + " " };
        }

        yield return new AgentStreamChunk
        {
            CompletedResponse = new AgentResponse
            {
                Text = text,
                Usage = new TokenUsage { InputTokens = _inputTokens, OutputTokens = _outputTokens }
            }
        };
    }
}
