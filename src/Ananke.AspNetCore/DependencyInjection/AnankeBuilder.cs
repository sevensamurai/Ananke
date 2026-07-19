using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Healing;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Ananke.AspNetCore.DependencyInjection;

internal sealed class AnankeBuilder(IServiceCollection services) : IAnankeBuilder
{
    public IServiceCollection Services { get; } = services;

    public IAnankeBuilder AddOrganicHost(Action<OrganicGrowthOptions>? configure = null)
    {
        Services.AddSingleton<IWorkflowHost, InProcessWorkflowHost>();
        Services.AddSingleton<IHealthMonitor, WorkflowExecutionMonitor>();
        Services.AddSingleton<IDivisionPolicy, ThresholdDivisionPolicy>();
        Services.AddSingleton<IDivisionApprovalGate, AutoApprovalGate>();

        Services.TryAddSingleton<ICapabilityMap>(sp =>
        {
            sp.GetService<ILoggerFactory>()
              ?.CreateLogger("Ananke.OrganicHost")
              .LogWarning(
                  "[Ananke] ICapabilityMap is backed by InMemoryCapabilityMap — " +
                  "colony mesh state will be lost on restart. " +
                  "Register a persistent ICapabilityMap before AddOrganicHost to suppress this warning.");
            return new InMemoryCapabilityMap();
        });
        Services.TryAddSingleton<IMeshAggregator>(sp =>
        {
            sp.GetService<ILoggerFactory>()
              ?.CreateLogger("Ananke.OrganicHost")
              .LogWarning(
                  "[Ananke] IMeshAggregator is backed by InMemoryMeshAggregator — " +
                  "metabolic signals will be lost on restart. " +
                  "Register a persistent IMeshAggregator before AddOrganicHost to suppress this warning.");
            return new InMemoryMeshAggregator();
        });
        Services.TryAddSingleton<ILineageStore>(sp =>
        {
            sp.GetService<ILoggerFactory>()
              ?.CreateLogger("Ananke.OrganicHost")
              .LogWarning(
                  "[Ananke] ILineageStore is backed by InMemoryLineageStore — " +
                  "cell lineage records will be lost on restart. " +
                  "Register a persistent ILineageStore before AddOrganicHost to suppress this warning.");
            return new InMemoryLineageStore();
        });

        Services.AddSingleton(sp =>
        {
            var options = new OrganicGrowthOptions
            {
                Policy = sp.GetRequiredService<IDivisionPolicy>(),
                ApprovalGate = sp.GetRequiredService<IDivisionApprovalGate>(),
                Monitor = sp.GetRequiredService<IHealthMonitor>(),
                MeshAggregator = sp.GetRequiredService<IMeshAggregator>(),
                Lineage = sp.GetRequiredService<ILineageStore>()
            };
            configure?.Invoke(options);
            return options;
        });

        Services.AddSingleton<OrganicHost>(sp =>
        {
            var host = sp.GetRequiredService<IWorkflowHost>();
            var landscape = sp.GetRequiredService<ICapabilityMap>();
            var options = sp.GetRequiredService<OrganicGrowthOptions>();
            return new OrganicHost(host, landscape, options);
        });

        return this;
    }
}
