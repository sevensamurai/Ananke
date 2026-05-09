using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Gating;

namespace Ananke.Design.Tools;

/// <summary>
/// Resolves manifest-declared tools into per-job <see cref="ToolKit"/> instances.
/// </summary>
public static class WorkflowToolResolver
{
    /// <summary>
    /// Resolves all job tool references in the manifest using the supplied resolver.
    /// </summary>
    /// <param name="manifest">The parsed workflow manifest.</param>
    /// <param name="resolver">Resolver used to convert manifest tool entries into runtime tools.</param>
    /// <param name="memory">
    /// Optional tool memory attached to each kit. Required when any job declares a
    /// <c>semantic_recall</c> router stage.
    /// </param>
    /// <param name="modelResolver">
    /// Optional model resolver used to materialise <c>llm</c> router stages.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A dictionary keyed by job name. Jobs without tool references are omitted.
    /// Each value is a <see cref="ToolKit"/> containing the resolved tools for that job,
    /// with a smart router attached when the job declares a <c>router:</c> block.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a job references an unknown tool key, when a referenced tool cannot be
    /// resolved, or when a router stage descriptor is invalid.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, ToolKit>> ResolveJobToolKitsAsync(
        WorkflowManifest manifest,
        IToolBindingResolver resolver,
        IToolMemory? memory = null,
        Func<string, IAgentModel?>? modelResolver = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(resolver);

        var kits = new Dictionary<string, ToolKit>(StringComparer.OrdinalIgnoreCase);

        foreach (var (jobName, job) in manifest.Jobs)
        {
            if (job.Tools.Count == 0)
                continue;

            var kit = new ToolKit($"{manifest.Name}:{jobName}:tools");

            if (memory is not null)
                kit.WithMemory(memory);

            foreach (var toolKey in job.Tools)
            {
                if (!manifest.Tools.TryGetValue(toolKey, out var entry))
                    throw new InvalidOperationException($"Job '{jobName}' references unknown tool '{toolKey}'.");

                var resolved = await resolver.ResolveAsync(entry, ct).ConfigureAwait(false);
                if (resolved is null)
                    throw new InvalidOperationException($"Tool '{toolKey}' could not be resolved.");

                kit.AddTool(resolved);
            }

            if (job.Router.Count > 0)
            {
                var router = RouterStageFactory.Build(
                    job.Router,
                    memory,
                    modelResolver);
                kit.WithRouter(router);
            }

            kits[jobName] = kit;
        }

        return kits;
    }
}
