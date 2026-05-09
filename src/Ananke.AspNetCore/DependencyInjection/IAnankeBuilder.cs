using Ananke.Organics.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace Ananke.AspNetCore.DependencyInjection;

/// <summary>
/// Fluent builder returned by
/// <see cref="AnankeServiceCollectionExtensions.AddAnanke"/> that allows
/// opt-in registration of additional Ananke subsystems.
/// </summary>
public interface IAnankeBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers <see cref="OrganicHost"/> and its default dependencies
    /// as singletons with in-memory defaults: <c>InProcessWorkflowHost</c>,
    /// <c>WorkflowExecutionMonitor</c>, <c>ThresholdDivisionPolicy</c>,
    /// <c>AutoApprovalGate</c>, <c>InMemoryMeshAggregator</c>,
    /// <c>InMemoryLineageStore</c>.
    /// </summary>
    IAnankeBuilder AddOrganicHost(Action<OrganicGrowthOptions>? configure = null);
}
