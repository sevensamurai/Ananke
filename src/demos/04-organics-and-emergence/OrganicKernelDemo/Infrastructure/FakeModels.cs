using Ananke.Abstractions.Agents;

namespace OrganicKernelDemo.Infrastructure;

/// <summary>Minimal fake model that returns a fixed text response (no API key needed).</summary>
sealed class FakeAgentModel(string responseText) : IAgentModel
{
    public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
        Task.FromResult(new AgentResponse { Text = responseText });
}

/// <summary>
/// Deterministic fake embedding model for demos (no API key needed).
/// Produces a stable unit vector derived from the input text hash.
/// </summary>
sealed class FakeEmbeddingModel : IEmbeddingModel
{
    private const int Dimensions = 16;

    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<ReadOnlyMemory<float>>(Embed(text));

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(
            texts.Select(t => (ReadOnlyMemory<float>)Embed(t)).ToList());

    private static float[] Embed(string text)
    {
        var rng = new Random(text.GetHashCode());
        var vec = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++)
            vec[i] = (float)rng.NextDouble();
        var mag = MathF.Sqrt(vec.Sum(v => v * v));
        for (var i = 0; i < Dimensions; i++)
            vec[i] /= mag;
        return vec;
    }
}

/// <summary>
/// Simulates what a real LLM would return when asked to design a workflow from a prompt.
/// Inspects the user message for tool names and generates valid host snapshot YAML.
/// In production, replace with a real model (OpenAI, Anthropic, etc.).
/// </summary>
sealed class FakeDesignerModel : IAgentModel
{
    public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var userMessage = request.Messages[0].Content ?? "";
        var tools = new List<string>();
        string[] knownTools =
        [
            "search_catalog", "get_book_details", "check_inventory", "get_recommendations",
            "process_payment", "create_order", "track_shipment", "apply_discount",
            "manage_returns", "customer_lookup"
        ];

        foreach (var tool in knownTools)
        {
            if (userMessage.Contains(tool, StringComparison.OrdinalIgnoreCase))
                tools.Add(tool);
        }

        if (tools.Count == 0)
        {
            string[] catalogKeywords = ["catalog", "search", "book", "inventory", "recommend"];
            if (catalogKeywords.Any(k => userMessage.Contains(k, StringComparison.OrdinalIgnoreCase)))
                tools.AddRange(["search_catalog", "get_book_details", "check_inventory", "get_recommendations"]);
        }

        if (tools.Count == 0)
            tools.AddRange(["search_catalog", "get_book_details", "check_inventory", "get_recommendations"]);

        var toolYaml = string.Join("\n", tools.Select(t => $"      - {t}"));

        var yaml = $"""
            kernel: bookstore
            version: 1
            taken_at: {DateTimeOffset.UtcNow:O}

            cells:
              bookstore-general:
                domain: bookstore
                tools:
            {toolYaml}
                models:
                  default:
                    provider: openai
                    model: gpt-4o-mini
                jobs:
                  handle-request:
                    type: agent
                    model: default
                    system_prompt: |
                      You are a helpful bookstore assistant. Answer customer questions
                      using the tools available to you. Be concise and friendly.
                  respond:
                    type: code
                connections:
                  - handle-request -> respond
            """;

        return Task.FromResult(new AgentResponse { Text = yaml });
    }
}
