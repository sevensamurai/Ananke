using Ananke.Organics.Division;
using Microsoft.Extensions.DependencyInjection;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Healing;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;

namespace Ananke.AspNetCore.DependencyInjection;

internal sealed class AnankeBuilder(IServiceCollection services) : IAnankeBuilder
{
    public IServiceCollection Services { get; } = services;

    public IAnankeBuilder AddOrganicHost(Action<OrganicGrowthOptions>? configure = null)
    {
        Services.AddSingleton<IWorkflowHost, InProcessWorkflowHost>();
        Services.AddSingleton<ICapabilityMap, InMemoryCapabilityMap>();
        Services.AddSingleton<IHealthMonitor, WorkflowExecutionMonitor>();
        Services.AddSingleton<IDivisionPolicy, ThresholdDivisionPolicy>();
        Services.AddSingleton<IDivisionApprovalGate, AutoApprovalGate>();
        Services.AddSingleton<IMeshAggregator, InMemoryMeshAggregator>();
        Services.AddSingleton<ILineageStore, InMemoryLineageStore>();

        Services.AddSingleton(sp =>
        {
            var options = new OrganicGrowthOptions
            {
                Policy         = sp.GetRequiredService<IDivisionPolicy>(),
                ApprovalGate   = sp.GetRequiredService<IDivisionApprovalGate>(),
                Monitor        = sp.GetRequiredService<IHealthMonitor>(),
                MeshAggregator = sp.GetRequiredService<IMeshAggregator>(),
                Lineage        = sp.GetRequiredService<ILineageStore>()
            };
            configure?.Invoke(options);
            return options;
        });

        Services.AddSingleton<OrganicHost>(sp =>
        {
            var host      = sp.GetRequiredService<IWorkflowHost>();
            var landscape = sp.GetRequiredService<ICapabilityMap>();
            var options   = sp.GetRequiredService<OrganicGrowthOptions>();
            return new OrganicHost(host, landscape, options);
        });

        return this;
    }
}
