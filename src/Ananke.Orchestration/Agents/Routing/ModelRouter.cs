using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Routing;

public sealed class ModelRouter : IModelRouter
{
    private readonly List<ModelRoute> _routes = [];
    private IAgentModel? _fallback;

    public ModelRouter When(Func<AgentRequest, bool> predicate, IAgentModel model)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(model);
        _routes.Add(new(predicate, model));
        return this;
    }

    public ModelRouter Otherwise(IAgentModel fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        _fallback = fallback;
        return this;
    }

    public IAgentModel Select(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var route in _routes)
        {
            if (route.Predicate(request))
                return route.Model;
        }

        return _fallback
            ?? throw new InvalidOperationException(
                "No model route matched the request and no fallback is configured. Call Otherwise() to set a default model.");
    }

    public IAgentModel ToAgentModel() => new RoutedAgentModel(this);

    private sealed record ModelRoute(Func<AgentRequest, bool> Predicate, IAgentModel Model);
}

public sealed class RoutedAgentModel : IStreamingAgentModel
{
    private readonly IModelRouter _router;
    private readonly IModelCostResolver? _costResolver;

    public RoutedAgentModel(IModelRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
        _costResolver = router as IModelCostResolver;
    }

    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var response = await _router.Select(request).GenerateAsync(request, ct);
        AccumulateCostIfAvailable(request, response);
        return response;
    }

    public IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(AgentRequest request, CancellationToken ct = default)
    {
        var model = _router.Select(request);
        return model is IStreamingAgentModel streaming
            ? WrapStreamForCost(streaming, request, ct)
            : BufferAsync(model, request, ct);
    }

    private async IAsyncEnumerable<AgentStreamChunk> WrapStreamForCost(
        IStreamingAgentModel model,
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in model.GenerateStreamAsync(request, ct))
        {
            if (chunk.CompletedResponse is not null)
                AccumulateCostIfAvailable(request, chunk.CompletedResponse);
            yield return chunk;
        }
    }

    private static async IAsyncEnumerable<AgentStreamChunk> BufferAsync(
        IAgentModel model,
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await model.GenerateAsync(request, ct);
        if (response.Text is not null)
            yield return new AgentStreamChunk { TextDelta = response.Text };
        yield return new AgentStreamChunk { CompletedResponse = response };
    }

    private void AccumulateCostIfAvailable(AgentRequest request, AgentResponse response)
    {
        if (_costResolver is null || response.Usage is null || TokenUsageCapture.Current.Value is null)
            return;

        var rates = _costResolver.ResolveCostRates(request);
        TokenUsageCapture.Current.Value.AddCost(rates.EstimateCost(response.Usage));
    }
}

public static class AgentRequestExtensions
{
    public static bool HasTools(this AgentRequest request) =>
        request.Tools is { Count: > 0 };

    public static int ToolCount(this AgentRequest request) =>
        request.Tools?.Count ?? 0;

    public static int MessageCount(this AgentRequest request) =>
        request.Messages.Count;

    public static bool HasStructuredOutput(this AgentRequest request) =>
        request.ResponseFormat is not null;

    public static int EstimatedContentLength(this AgentRequest request) =>
        (request.SystemPrompt?.Length ?? 0) +
        request.Messages.Sum(m => m.Content?.Length ?? 0);

    public static bool HasSystemPrompt(this AgentRequest request) =>
        !string.IsNullOrEmpty(request.SystemPrompt);

    /// <summary>
    /// Tags the request with explicit <see cref="ModelCapability"/> requirements.
    /// These are merged with structurally inferred capabilities by
    /// <see cref="TaskRequirements.InferFrom"/>.
    /// </summary>
    public static AgentRequest WithRequiredCapabilities(this AgentRequest request, ModelCapability capabilities)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request with { Metadata = WithMeta(request.Metadata, "required_capabilities", capabilities.ToString()) };
    }

    /// <summary>
    /// Tags the request with a minimum intelligence tier (1–5) for capability-based routing.
    /// </summary>
    public static AgentRequest WithMinIntelligence(this AgentRequest request, int tier)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tier, 5);
        return request with { Metadata = WithMeta(request.Metadata, "min_intelligence", tier.ToString()) };
    }

    /// <summary>
    /// Tags the request with a minimum context window requirement for capability-based routing.
    /// </summary>
    public static AgentRequest WithMinContextTokens(this AgentRequest request, int tokens)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(tokens, 1);
        return request with { Metadata = WithMeta(request.Metadata, "min_context_tokens", tokens.ToString()) };
    }

    private static IReadOnlyDictionary<string, string> WithMeta(
        IReadOnlyDictionary<string, string>? existing, string key, string value)
    {
        var dict = existing is not null
            ? new Dictionary<string, string>(existing)
            : [];
        dict[key] = value;
        return dict;
    }
}
