using Ananke.Abstractions.Tracing;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ananke.Orchestration.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFlowOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowRunner>(sp =>
        {
            var store = sp.GetService<ICheckpointStore>();
            var tracer = sp.GetService<IWorkflowTracer>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new WorkflowRunner(store, tracer: tracer, loggerFactory: loggerFactory);
        });
        return services;
    }

    public static IServiceCollection AddFlowOrchestration(
        this IServiceCollection services,
        Action<OrchestrationOptions> configure)
    {
        var options = new OrchestrationOptions();
        configure(options);

        if (options.UseInMemoryCheckpoints)
            services.AddSingleton<ICheckpointStore, InMemoryCheckpointStore>();

        services.AddSingleton<IWorkflowRunner>(sp =>
        {
            var store = sp.GetService<ICheckpointStore>();
            var tracer = sp.GetService<IWorkflowTracer>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new WorkflowRunner(store, tracer: tracer,
                storeCompletions: options.StoreCompletionsEnabled,
                loggerFactory: loggerFactory,
                checkpointTtl: options.CheckpointTtl);
        });

        return services;
    }
}

public class OrchestrationOptions
{
    internal bool UseInMemoryCheckpoints { get; private set; }
    internal bool StoreCompletionsEnabled { get; private set; } = true;
    internal TimeSpan CheckpointTtl { get; private set; } = TimeSpan.FromDays(7);

    public OrchestrationOptions UseCheckpointing()
    {
        UseInMemoryCheckpoints = true;
        return this;
    }

    /// <summary>
    /// Controls whether LLM completions are stored in the provider's platform logs
    /// (e.g. <see href="https://platform.openai.com/logs"/>). Default is <c>true</c>.
    /// </summary>
    public OrchestrationOptions StoreCompletions(bool enabled)
    {
        StoreCompletionsEnabled = enabled;
        return this;
    }

    /// <summary>
    /// Sets how long checkpoints are retained after creation. Defaults to 7 days.
    /// </summary>
    public OrchestrationOptions WithCheckpointTtl(TimeSpan ttl)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        CheckpointTtl = ttl;
        return this;
    }
}
