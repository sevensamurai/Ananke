using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Gating;
using Ananke.Orchestration.Tools.Routing;

namespace Ananke.Design.Tools;

/// <summary>
/// Materialises a <see cref="CompositeSmartToolRouter"/> from a list of
/// <see cref="RouterStageDescriptor"/> instances declared in the manifest
/// </summary>
public static class RouterStageFactory
{
    /// <summary>
    /// Builds a <see cref="CompositeSmartToolRouter"/> from the supplied descriptors.
    /// </summary>
    /// <param name="stages">Ordered stage descriptors from the manifest.</param>
    /// <param name="memory">
    /// Tool memory used by <c>semantic_recall</c> stages.
    /// May be <see langword="null"/> when no semantic recall stage is present.
    /// </param>
    /// <param name="modelResolver">
    /// Resolves a model alias key to an <see cref="IAgentModel"/> instance.
    /// Required for <c>llm</c> stages; may be <see langword="null"/> otherwise.
    /// </param>
    /// <param name="tracker">
    /// Shared affinity tracker used by <c>affinity_rerank</c> stages.
    /// When <see langword="null"/>, a default tracker is created.
    /// </param>
    /// <returns>A composite router wrapping the materialised stage chain.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown on unknown stage kind (<c>ANANKE_ROUTER_001</c>) or a missing model
    /// reference in an <c>llm</c> stage (<c>ANANKE_ROUTER_002</c>).
    /// </exception>
    public static CompositeSmartToolRouter Build(
        IReadOnlyList<RouterStageDescriptor> stages,
        IToolMemory? memory = null,
        Func<string, IAgentModel?>? modelResolver = null,
        ToolAffinityTracker? tracker = null)
    {
        ArgumentNullException.ThrowIfNull(stages);

        var materialised = new List<ISmartToolRouter>(stages.Count);

        foreach (var descriptor in stages)
        {
            var stage = descriptor switch
            {
                PinnedStageDescriptor p =>
                    (ISmartToolRouter)new PinnedToolStage(p.Tools),

                HealthFilterStageDescriptor =>
                    new HealthFilterStage(),

                SemanticRecallStageDescriptor s =>
                    memory is not null
                        ? new SemanticRecallStage(memory, s.TopK)
                        : throw new InvalidOperationException(
                            "ANANKE_ROUTER_001: semantic_recall stage requires IToolMemory but none was provided."),

                AffinityRerankStageDescriptor =>
                    new AffinityRerankStage(tracker ?? new ToolAffinityTracker()),

                HeuristicTagsStageDescriptor =>
                    new HeuristicTagStage(msg =>
                    {
                        var tokens = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        return new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
                    }),

                LlmStageDescriptor l =>
                    BuildLlmStage(l, modelResolver),

                _ => throw new InvalidOperationException(
                    $"ANANKE_ROUTER_001: Unknown router stage kind '{descriptor.Kind}'."),
            };

            materialised.Add(stage);
        }

        return new CompositeSmartToolRouter(materialised);
    }

    private static LlmRouterStage BuildLlmStage(
        LlmStageDescriptor descriptor,
        Func<string, IAgentModel?>? modelResolver)
    {
        if (modelResolver is null)
            throw new InvalidOperationException(
                $"ANANKE_ROUTER_002: LLM stage references model '{descriptor.Model}' but no model resolver was provided.");

        var model = modelResolver(descriptor.Model)
            ?? throw new InvalidOperationException(
                $"ANANKE_ROUTER_002: LLM stage references model '{descriptor.Model}' which could not be resolved.");

        return new LlmRouterStage(model);
    }
}
